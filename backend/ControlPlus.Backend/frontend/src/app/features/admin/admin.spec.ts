import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Admin } from './admin';
import { AdminApi, AdminRole, AdminUser } from './admin-api';
import { SessionService } from '../../core/session.service';
import { adminGuard } from '../../core/guards';

describe('Admin: usuarios, roles y permisos', () => {
  const api = { users: vi.fn(), roles: vi.fn(), createUser: vi.fn(), updateUser: vi.fn(), setUserActive: vi.fn(),
    reactivateUser: vi.fn(), assignRole: vi.fn(), changePassword: vi.fn(), deleteUser: vi.fn() };
  const state = { id: 'admin-1', role: 'ADMINISTRADOR', permissions: ['USERS.READ', 'USERS.MANAGE', 'ROLES.READ'] };
  const session = { has: (code: string) => state.permissions.includes(code), hasRole: (...codes: string[]) => codes.includes(state.role), user: () => ({ id: state.id }) };
  const role = (id: string, name: string, level: number, isActive = true): AdminRole => ({ id, code: name.toUpperCase(), name, level, isActive,
    permissions: [{ id: `${id}-p`, code: 'PRODUCTS.READ', name: 'Consultar productos', description: null, module: 'PRODUCTS', isActive: true }] });
  const roles = [role('r-admin', 'Administrador', 3), role('r-cash', 'Cajero', 1), role('r-old', 'Antiguo', 2, false)];
  const user = (id: string, extra: Partial<AdminUser> = {}): AdminUser => ({ id, userName: id, displayName: `Nombre ${id}`, isActive: true, isLocked: false,
    failedLoginAttempts: 0, lockedAtUtc: null, lastLoginAtUtc: null, roles: [{ id: 'r-cash', code: 'CAJERO', name: 'Cajero', level: 1, isActive: true }], ...extra });
  let component: Admin;

  beforeEach(() => {
    vi.clearAllMocks();
    state.id = 'admin-1'; state.role = 'ADMINISTRADOR'; state.permissions = ['USERS.READ', 'USERS.MANAGE', 'ROLES.READ'];
    api.users.mockResolvedValue({ items: [user('ana'), user('bloqueado', { isLocked: true })], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 });
    api.roles.mockResolvedValue({ items: roles, page: 1, pageSize: 50, totalCount: 3, totalPages: 1 });
    TestBed.configureTestingModule({ providers: [{ provide: AdminApi, useValue: api }, { provide: SessionService, useValue: session }] });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    component = TestBed.runInInjectionContext(() => new Admin());
  });

  it('carga usuarios y roles al abrir', async () => {
    await component.ngOnInit();
    expect(component.users()?.items.length).toBe(2);
    expect(component.roles().length).toBe(3);
    expect(component.stateLabel(user('x', { isActive: false }))).toBe('Inactivo');
    expect(component.stateLabel(user('x', { isLocked: true }))).toBe('Bloqueado');
    expect(component.levelName(3)).toBe('Administrador');
  });

  it('crea un usuario con un único rol y exige confirmar la contraseña', async () => {
    await component.ngOnInit();
    api.createUser.mockResolvedValue(user('nuevo'));
    component.create();
    component.form = { ...component.form, userName: ' nuevo ', displayName: ' Nuevo ', password: 'clave', confirmPassword: 'otra', roleId: 'r-cash' };
    await component.save();
    expect(api.createUser).not.toHaveBeenCalled();
    expect(component.error()).toContain('confirmación');
    component.form.confirmPassword = 'clave';
    await component.save();
    expect(api.createUser).toHaveBeenCalledWith({ userName: 'nuevo', displayName: 'Nuevo', password: 'clave', roleIds: ['r-cash'], email: null });
    expect(component.mode()).toBeNull();
    expect(component.notice()).toBe('Usuario creado.');
  });

  it('no ofrece crear ni cambiar rol sin ROLES.READ ni gestionar sin USERS.MANAGE', async () => {
    state.permissions = ['USERS.READ', 'USERS.MANAGE'];
    await component.ngOnInit();
    component.create();
    expect(component.mode()).toBeNull();
    state.permissions = ['USERS.READ'];
    component.edit(user('ana'));
    component.resetPassword(user('ana'));
    expect(component.mode()).toBeNull();
  });

  it('solo pide la contraseña actual cuando cambia la propia', async () => {
    api.changePassword.mockResolvedValue(undefined);
    component.resetPassword(user('ana'));
    component.form = { ...component.form, password: 'nueva', confirmPassword: 'nueva' };
    await component.save();
    expect(api.changePassword).toHaveBeenCalledWith('ana', 'nueva', '');
    component.resetPassword(user('admin-1'));
    component.form = { ...component.form, password: 'nueva', confirmPassword: 'nueva' };
    await component.save();
    expect(component.error()).toContain('contraseña actual');
    component.form.currentPassword = 'actual';
    await component.save();
    expect(api.changePassword).toHaveBeenLastCalledWith('admin-1', 'nueva', 'actual');
  });

  it('confirma el cambio de rol y lo envía', async () => {
    await component.ngOnInit();
    api.assignRole.mockResolvedValue(user('ana'));
    component.changeRole(user('ana'));
    component.form.roleId = 'r-admin';
    await component.save();
    expect(window.confirm).toHaveBeenCalled();
    expect(api.assignRole).toHaveBeenCalledWith('ana', 'r-admin');
    vi.mocked(window.confirm).mockReturnValue(false);
    api.assignRole.mockClear();
    component.changeRole(user('ana')); component.form.roleId = 'r-cash';
    await component.save();
    expect(api.assignRole).not.toHaveBeenCalled();
  });

  it('inactiva, activa y reactiva acceso solo tras confirmar', async () => {
    await component.ngOnInit();
    api.setUserActive.mockResolvedValue(user('ana')); api.reactivateUser.mockResolvedValue(user('bloqueado'));
    await component.toggle(user('ana'));
    await component.unlock(user('bloqueado', { isLocked: true }));
    expect(api.setUserActive).toHaveBeenCalledOnce();
    expect(api.reactivateUser).toHaveBeenCalledWith('bloqueado');
    vi.mocked(window.confirm).mockReturnValue(false);
    await component.toggle(user('ana'));
    expect(api.setUserActive).toHaveBeenCalledOnce();
  });

  it('registra, valida y quita el correo del usuario', async () => {
    await component.ngOnInit();
    api.createUser.mockResolvedValue(user('nuevo')); api.updateUser.mockResolvedValue(user('ana'));
    component.create();
    component.form = { ...component.form, userName: 'nuevo', displayName: 'Nuevo', email: 'sin arroba', password: 'clave', confirmPassword: 'clave', roleId: 'r-cash' };
    await component.save();
    expect(api.createUser).not.toHaveBeenCalled();
    expect(component.error()).toBe('El correo no es válido.');
    component.form.email = ' nuevo@example.com ';
    await component.save();
    expect(api.createUser).toHaveBeenCalledWith(expect.objectContaining({ email: 'nuevo@example.com' }));

    component.edit({ ...user('ana'), email: 'ana@example.com' });
    expect(component.form.email).toBe('ana@example.com');
    component.form.email = '';
    await component.save();
    expect(api.updateUser).toHaveBeenCalledWith('ana', expect.any(String), null);
  });

  it('solo el Administrador elimina usuarios, nunca a sí mismo, y el historial lo impide con un mensaje', async () => {
    await component.ngOnInit();
    api.deleteUser.mockResolvedValueOnce(undefined);
    await component.remove(user('nuevo'));
    expect(api.deleteUser).toHaveBeenCalledWith('nuevo');
    expect(component.notice()).toBe('Usuario eliminado.');

    api.deleteUser.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'El usuario ya tiene historial y no se puede eliminar.' } }));
    await component.remove(user('ana'));
    expect(component.error()).toContain('historial');

    await component.remove(user('admin-1'));
    expect(api.deleteUser).toHaveBeenCalledTimes(2);
    state.role = 'SUPERVISOR';
    expect(component.canDeleteUsers).toBe(false);
    await component.remove(user('otro'));
    expect(api.deleteUser).toHaveBeenCalledTimes(2);
  });

  it('muestra el error de la API sin cerrar el formulario', async () => {
    api.updateUser.mockRejectedValue(new Error('fallo'));
    component.edit(user('ana'));
    await component.save();
    expect(component.mode()).toBe('edit');
    expect(component.error()).not.toBe('');
  });

  it('agrupa los permisos de un rol por módulo en modo consulta', async () => {
    await component.ngOnInit();
    component.viewRole(roles[0]);
    expect(component.viewedRole()?.id).toBe('r-admin');
    expect(component.permissionModules(roles[0])).toEqual([{ module: 'PRODUCTS', permissions: roles[0].permissions }]);
    component.viewRole(roles[0]);
    expect(component.viewedRole()).toBeNull();
  });

  it('solo con AUDIT.READ abre directamente la pestaña de Auditoría', async () => {
    state.permissions = ['AUDIT.READ'];
    await component.ngOnInit();
    expect(component.tab()).toBe('audit');
    expect(api.users).not.toHaveBeenCalled();
  });

  it('la ruta exige USERS.READ, ROLES.READ o AUDIT.READ', () => {
    for (const [permissions, allowed] of [[['USERS.READ'], true], [['ROLES.READ'], true], [['AUDIT.READ'], true], [['USERS.MANAGE'], false]] as const) {
      state.permissions = [...permissions];
      expect(TestBed.runInInjectionContext(() => adminGuard({} as never, {} as never))).toBe(allowed);
    }
  });
});
