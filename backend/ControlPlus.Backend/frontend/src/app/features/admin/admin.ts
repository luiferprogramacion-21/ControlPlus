import { RouterLink } from '@angular/router';
import { groupedNumber } from '../../shared/number-input';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { AdminApi, AdminPermission, AdminRole, AdminUser, ROLE_LEVEL_NAMES } from './admin-api';
import { AuditLog } from './audit-log';
import { RoleTemplate } from './role-template';
import { UserCredential } from './user-credential';
import { UserPermissions } from './user-permissions';
import { isVisibleAdminPermission } from './visible-permissions';
import { toastSignal } from '../../core/toast';
import { ConfirmationDialog, ConfirmationOptions } from '../../shared/confirmation-dialog';
import { WindowSheet } from '../../shared/window-sheet';

type Mode = 'create' | 'edit' | 'role' | 'password';
type Tab = 'users' | 'roles' | 'audit';

@Component({
  selector: 'cp-admin',
  imports: [WindowSheet, DatePipe, FormsModule, MatButtonModule, MatPaginatorModule, RouterLink, Icon, RoleTemplate, UserPermissions, UserCredential, AuditLog],
  templateUrl: './admin.html',
  styleUrls: ['../directory.scss', './admin.scss'],
})
export class Admin implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly confirmations = inject(ConfirmationDialog);
  private readonly destroyRef = inject(DestroyRef);
  private confirming = false;
  readonly session = inject(SessionService);
  readonly number = groupedNumber;
  readonly tab = signal<Tab>('users');
  readonly users = signal<Page<AdminUser> | null>(null);
  readonly roles = signal<AdminRole[]>([]);
  readonly viewedRole = signal<AdminRole | null>(null);
  readonly selected = signal<AdminUser | null>(null);
  readonly catalog = signal<AdminPermission[]>([]);
  readonly permissionUser = signal<AdminUser | null>(null);
  readonly credentialUser = signal<AdminUser | null>(null);
  readonly mode = signal<Mode | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  search = ''; status: 'all' | 'active' | 'inactive' | 'locked' = 'all'; page = 1; pageSize = 20;
  form = { userName: '', displayName: '', email: '', password: '', confirmPassword: '', currentPassword: '', roleId: '' };
  private requestId = 0;

  get canReadUsers(): boolean { return this.session.has('USERS.READ'); }
  get canReadRoles(): boolean { return this.session.has('ROLES.READ'); }
  get canReadAudit(): boolean { return this.session.has('AUDIT.READ'); }
  get canManageUsers(): boolean { return this.session.has('USERS.MANAGE'); }
  /** Creating or reassigning needs the role catalogue to choose from. */
  get canChooseRole(): boolean { return this.canManageUsers && this.canReadRoles; }
  /** Individual exceptions and template editing need the permission catalogue to be readable. */
  get canManageUserPermissions(): boolean { return this.session.has('USER_PERMISSIONS.MANAGE') && this.session.has('PERMISSIONS.READ'); }
  get canEditTemplates(): boolean { return this.session.has('ROLES.MANAGE') && this.session.has('PERMISSIONS.READ'); }

  async ngOnInit(): Promise<void> {
    if (!this.canReadUsers) this.tab.set(this.canReadRoles ? 'roles' : 'audit');
    await Promise.all([this.reload(), this.canReadRoles ? this.loadRoles() : Promise.resolve()]);
  }

  levelName(level: number): string { return ROLE_LEVEL_NAMES[level] ?? 'Sin nivel'; }
  stateLabel(user: AdminUser): string { return !user.isActive ? 'Inactivo' : user.isLocked ? 'Bloqueado' : 'Activo'; }
  isSelf(user: AdminUser): boolean { return user.id === this.session.user()?.id; }
  permissionModules(role: AdminRole): { module: string; permissions: AdminRole['permissions'] }[] {
    const groups = new Map<string, AdminRole['permissions']>();
    for (const permission of role.permissions.filter(item => isVisibleAdminPermission(item.code))) {
      groups.set(permission.module, [...(groups.get(permission.module) ?? []), permission]);
    }
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([module, permissions]) => ({ module, permissions }));
  }

  switchTab(tab: Tab): void { this.tab.set(tab); this.close(); this.viewedRole.set(null); this.notice.set(''); this.error.set(''); }

  async reload(): Promise<void> {
    if (!this.canReadUsers) return;
    const id = ++this.requestId;
    this.loading.set(true); this.error.set('');
    try {
      const data = await this.api.users({
        search: this.search.trim(),
        isActive: this.status === 'active' ? true : this.status === 'inactive' ? false : null,
        isLocked: this.status === 'locked' ? true : null,
        page: this.page, pageSize: this.pageSize,
      });
      if (id === this.requestId) this.users.set(data);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  async loadRoles(): Promise<void> {
    try { this.roles.set((await this.api.roles({ page: 1, pageSize: 50 })).items.map(role => this.visibleRole(role))); }
    catch (e) { this.error.set(errorText(e)); }
  }

  find(): void { this.page = 1; void this.reload(); }
  pageChanged(event: PageEvent): void { this.page = event.pageIndex + 1; this.pageSize = event.pageSize; void this.reload(); }

  private open(mode: Mode, user: AdminUser | null): void {
    this.selected.set(user); this.mode.set(mode); this.error.set(''); this.notice.set('');
    this.form = { userName: '', displayName: user?.displayName ?? '', email: user?.email ?? '', password: '', confirmPassword: '', currentPassword: '', roleId: user?.roles[0]?.id ?? '' };
  }
  create(): void { if (this.canChooseRole) this.open('create', null); }
  edit(user: AdminUser): void { if (this.canManageUsers) this.open('edit', user); }
  changeRole(user: AdminUser): void { if (this.canChooseRole) this.open('role', user); }
  resetPassword(user: AdminUser): void { if (this.canManageUsers) this.open('password', user); }
  close(): void { this.mode.set(null); this.selected.set(null); this.form = { ...this.form, password: '', confirmPassword: '', currentPassword: '' }; }

  validation(): string {
    const f = this.form;
    switch (this.mode()) {
      case 'create':
        if (!f.userName.trim()) return 'Indica el nombre de usuario.';
        if (!f.displayName.trim()) return 'Indica el nombre visible.';
        if (!f.roleId) return 'Selecciona un rol.';
        return this.emailIssue() || this.passwordIssue();
      case 'edit': return f.displayName.trim() ? this.emailIssue() : 'Indica el nombre visible.';
      case 'role': return f.roleId ? '' : 'Selecciona un rol.';
      case 'password': {
        const self = this.selected() && this.isSelf(this.selected()!);
        if (self && !f.currentPassword) return 'Indica tu contraseña actual.';
        return this.passwordIssue();
      }
      default: return '';
    }
  }
  /** El correo es opcional; si se escribe, debe parecer una dirección (el servidor también lo valida). */
  private emailIssue(): string {
    const email = this.form.email.trim();
    return !email || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email) ? '' : 'El correo no es válido.';
  }
  private passwordIssue(): string {
    if (!this.form.password.trim()) return 'Indica la contraseña.';
    return this.form.password === this.form.confirmPassword ? '' : 'La confirmación no coincide con la contraseña.';
  }

  async save(): Promise<void> {
    const mode = this.mode(); const user = this.selected();
    if (!mode || this.saving() || this.confirming) return;
    const issue = this.validation();
    if (issue) { this.error.set(issue); return; }
    const f = { ...this.form };
    if (mode === 'role' && user) {
      const approved = await this.confirmChange({ title: 'Cambiar rol',
        message: `¿Cambiar el rol de ${user.displayName}? Sus sesiones abiertas se invalidarán.`, confirmText: 'Cambiar rol' });
      if (!approved || this.destroyRef.destroyed || this.saving() || !this.canChooseRole ||
        this.mode() !== mode || this.selected()?.id !== user.id || this.form.roleId !== f.roleId) return;
    }
    this.saving.set(true); this.error.set('');
    try {
      if (mode === 'create') await this.api.createUser({ userName: f.userName.trim(), displayName: f.displayName.trim(), password: f.password, roleIds: [f.roleId], email: f.email.trim() || null });
      else if (mode === 'edit' && user) await this.api.updateUser(user.id, f.displayName.trim(), f.email.trim() || null);
      else if (mode === 'role' && user) await this.api.assignRole(user.id, f.roleId);
      else if (mode === 'password' && user) await this.api.changePassword(user.id, f.password, this.isSelf(user) ? f.currentPassword : '');
      const message = mode === 'create' ? 'Usuario creado.' : mode === 'edit' ? 'Usuario actualizado.' : mode === 'role' ? 'Rol actualizado.' : 'Contraseña actualizada.';
      this.close(); this.notice.set(message);
      if (mode !== 'password') await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  async toggle(user: AdminUser): Promise<void> {
    if (!this.canManageUsers || this.saving() || this.confirming) return;
    const snapshot = { ...user };
    const approved = await this.confirmChange({ title: snapshot.isActive ? 'Inactivar usuario' : 'Activar usuario',
      message: `¿${snapshot.isActive ? 'Inactivar' : 'Activar'} a ${snapshot.displayName}?`,
      confirmText: snapshot.isActive ? 'Inactivar' : 'Activar', danger: snapshot.isActive });
    if (!approved || this.destroyRef.destroyed || this.saving() || !this.canManageUsers || user.isActive !== snapshot.isActive) return;
    await this.run(() => this.api.setUserActive(snapshot), 'Estado actualizado.');
  }
  /** Decisión del 18 de noviembre de 2026: solo el Administrador, y solo usuarios que nunca operaron. */
  get canDeleteUsers(): boolean { return this.canManageUsers && this.session.hasRole('ADMINISTRADOR'); }
  async remove(user: AdminUser): Promise<void> {
    if (!this.canDeleteUsers || this.saving() || this.confirming || this.isSelf(user)) return;
    const userId = user.id;
    const approved = await this.confirmChange({ title: 'Eliminar usuario',
      message: `¿Eliminar definitivamente a ${user.displayName}? Solo es posible si nunca ha trabajado en el sistema; si ya tiene historial, inactívelo.`,
      confirmText: 'Eliminar usuario', danger: true });
    if (!approved || this.destroyRef.destroyed || this.saving() || !this.canDeleteUsers || this.isSelf(user) || user.id !== userId) return;
    await this.run(() => this.api.deleteUser(userId), 'Usuario eliminado.');
  }
  async unlock(user: AdminUser): Promise<void> {
    if (!this.canManageUsers || this.saving() || this.confirming) return;
    const userId = user.id;
    const approved = await this.confirmChange({ title: 'Reactivar acceso',
      message: `¿Reactivar el acceso de ${user.displayName}?`, confirmText: 'Reactivar acceso' });
    if (!approved || this.destroyRef.destroyed || this.saving() || !this.canManageUsers || user.id !== userId) return;
    await this.run(() => this.api.reactivateUser(userId), 'Acceso reactivado.');
  }
  private async confirmChange(options: ConfirmationOptions): Promise<boolean> {
    if (this.confirming || this.saving()) return false;
    this.confirming = true;
    try { return await this.confirmations.confirm(options, this.destroyRef); }
    finally { this.confirming = false; }
  }
  private async run(action: () => Promise<unknown>, message: string): Promise<void> {
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { await action(); this.notice.set(message); await this.reload(); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  async viewRole(role: AdminRole): Promise<void> {
    if (this.viewedRole()?.id === role.id) { this.viewedRole.set(null); return; }
    this.viewedRole.set(role);
    if (this.canEditTemplates) await this.ensureCatalog();
  }

  async openPermissions(user: AdminUser): Promise<void> {
    if (!this.canManageUserPermissions) return;
    this.error.set(''); this.notice.set('');
    if (!(await this.ensureCatalog())) return;
    this.permissionUser.set(user);
  }
  closePermissions(): void { this.permissionUser.set(null); }
  openCredential(user: AdminUser): void { if (this.canManageUsers) { this.error.set(''); this.notice.set(''); this.credentialUser.set(user); } }
  closeCredential(): void { this.credentialUser.set(null); }

  roleUpdated(updated: AdminRole): void {
    updated = this.visibleRole(updated);
    this.roles.update(list => list.map(role => role.id === updated.id ? updated : role));
    this.viewedRole.set(updated);
    this.notice.set('Plantilla actualizada.');
  }

  private async ensureCatalog(): Promise<boolean> {
    if (this.catalog().length) return true;
    try { this.catalog.set((await this.api.permissionCatalog()).filter(permission => isVisibleAdminPermission(permission.code))); return true; }
    catch (e) { this.error.set(errorText(e)); return false; }
  }

  private visibleRole(role: AdminRole): AdminRole {
    return { ...role, permissions: role.permissions.filter(permission => isVisibleAdminPermission(permission.code)) };
  }
}
