import { Component, computed, inject, input, output, signal } from '@angular/core';
import { errorText } from '../../core/api';
import { AdminApi, AdminPermission, AdminRole } from './admin-api';

@Component({
  selector: 'cp-role-template',
  styleUrl: './admin.scss',
  template: `
    <div class="role-permissions" aria-label="Plantilla de permisos del rol">
      <div class="permissions-toolbar">
        <h3>Plantilla de {{ role().name }}</h3>
        <button class="button button--secondary" type="button" [disabled]="saving()" (click)="reset()">Restaurar plantilla original</button>
      </div>
      <p class="muted">Cada cambio se aplica de inmediato, se audita e invalida las sesiones de todos los usuarios con este rol.</p>
      @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
      <div class="permissions-list">
        @for (group of groups(); track group.module) {
          <h4>{{ group.module }}</h4>
          <ul>
            @for (permission of group.permissions; track permission.id) {
              <li><label><input type="checkbox" [checked]="granted().has(permission.id)" [disabled]="saving()" (change)="toggle(permission, $any($event.target))">
                <span>{{ permission.name }} <small>({{ permission.code }})</small></span></label></li>
            }
          </ul>
        }
      </div>
    </div>`,
})
export class RoleTemplate {
  private readonly api = inject(AdminApi);
  readonly role = input.required<AdminRole>();
  readonly catalog = input.required<AdminPermission[]>();
  readonly updated = output<AdminRole>();
  readonly saving = signal(false);
  readonly error = signal('');
  readonly granted = computed(() => new Set(this.role().permissions.map(permission => permission.id)));
  readonly groups = computed(() => {
    const map = new Map<string, AdminPermission[]>();
    for (const permission of this.catalog()) map.set(permission.module, [...(map.get(permission.module) ?? []), permission]);
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b))
      .map(([module, permissions]) => ({ module, permissions: permissions.sort((a, b) => a.code.localeCompare(b.code)) }));
  });

  /** The native checkbox is reverted on cancel or failure, since its bound value did not change. */
  async toggle(permission: AdminPermission, input: HTMLInputElement): Promise<void> {
    const checked = input.checked;
    if (this.saving()) { input.checked = !checked; return; }
    const role = this.role();
    if (!confirm(`${checked ? 'Añadir' : 'Quitar'} "${permission.name}" ${checked ? 'a' : 'de'} la plantilla de ${role.name}? Se invalidarán las sesiones de sus usuarios.`)) {
      input.checked = !checked;
      return;
    }
    const ok = await this.run(() => checked ? this.api.grantRolePermission(role.id, permission.id) : this.api.revokeRolePermission(role.id, permission.id));
    if (!ok) input.checked = !checked;
  }

  async reset(): Promise<void> {
    const role = this.role();
    if (this.saving() || !confirm(`Restaurar la plantilla original de ${role.name}? Se reemplazarán sus permisos actuales y se invalidarán las sesiones de sus usuarios.`)) return;
    await this.run(() => this.api.resetRolePermissions(role.id));
  }

  private async run(action: () => Promise<AdminRole>): Promise<boolean> {
    this.saving.set(true); this.error.set('');
    try { this.updated.emit(await action()); return true; }
    catch (e) { this.error.set(errorText(e)); return false; }
    finally { this.saving.set(false); }
  }
}
