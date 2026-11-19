import { ConfirmationDialog } from '../../shared/confirmation-dialog';
const confirmAction = vi.fn();
import { TestBed } from '@angular/core/testing';
import { Admin } from './admin';
import { AdminApi, AdminPermission, AdminRole, AdminUser, EffectivePermission } from './admin-api';
import { RoleTemplate } from './role-template';
import { UserPermissions } from './user-permissions';
import { SessionService } from '../../core/session.service';

const permission = (id: string, module: string, code = `${module}.${id}`): AdminPermission => ({ id, code, name: `Permiso ${id}`, description: null, module, isActive: true });
const catalog = [permission('a', 'CASH'), permission('b', 'SALES'), permission('c', 'SALES')];
const user = { id: 'u1', userName: 'ana', displayName: 'Ana', isActive: true, isLocked: false, failedLoginAttempts: 0, lockedAtUtc: null, lastLoginAtUtc: null, roles: [] } as AdminUser;
const role = (ids: string[]): AdminRole => ({ id: 'r1', code: 'CAJERO', name: 'Cajero', level: 1, isActive: true, permissions: catalog.filter(p => ids.includes(p.id)) });

describe('Administración: excepciones individuales', () => {
  const api = { effectivePermissions: vi.fn(), setPermissionOverride: vi.fn(), resetPermissionOverrides: vi.fn() };
  const effective = (over: Partial<EffectivePermission>[] = []): EffectivePermission[] => catalog.map(p => ({ id: p.id, code: p.code, name: p.name, granted: p.id === 'a', source: 'ROL', individualEffect: null, ...(over.find(o => o.id === p.id) ?? {}) }));
  const flush = async (fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) => { await fixture.whenStable(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };

  beforeEach(() => {
    vi.clearAllMocks();
    api.effectivePermissions.mockResolvedValue(effective());
    api.setPermissionOverride.mockResolvedValue(undefined);
    api.resetPermissionOverrides.mockResolvedValue(undefined);
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } }, { provide: AdminApi, useValue: api }] });
    confirmAction.mockResolvedValue(true);
  });

  function open() {
    const fixture = TestBed.createComponent(UserPermissions);
    fixture.componentRef.setInput('user', user);
    fixture.componentRef.setInput('catalog', catalog);
    fixture.detectChanges();
    return fixture;
  }

  it('agrupa por módulo y muestra el origen de cada permiso', async () => {
    api.effectivePermissions.mockResolvedValue(effective([{ id: 'b', granted: true, source: 'INDIVIDUAL', individualEffect: 'CONCEDER' }, { id: 'c', granted: false, source: 'INDIVIDUAL', individualEffect: 'REVOCAR' }]));
    const fixture = open(); await flush(fixture);
    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('h4')).map(x => x.textContent)).toEqual(['CASH', 'SALES']);
    expect(text).toContain('Por rol'); expect(text).toContain('Concedido individualmente'); expect(text).toContain('Revocado individualmente');
    expect(fixture.componentInstance.exceptionCount()).toBe(2);
  });

  it('invierte el permiso efectivo tras confirmar y recarga', async () => {
    const fixture = open(); await flush(fixture);
    await fixture.componentInstance.toggle(fixture.componentInstance.rows().find(r => r.id === 'a')!);
    expect(api.setPermissionOverride).toHaveBeenCalledWith('u1', 'a', false);
    await fixture.componentInstance.toggle(fixture.componentInstance.rows().find(r => r.id === 'b')!);
    expect(api.setPermissionOverride).toHaveBeenLastCalledWith('u1', 'b', true);
    expect(api.effectivePermissions).toHaveBeenCalledTimes(3);
  });

  it('no cambia nada si se cancela la confirmación', async () => {
    confirmAction.mockResolvedValue(false);
    const fixture = open(); await flush(fixture);
    await fixture.componentInstance.toggle(fixture.componentInstance.rows()[0]);
    await fixture.componentInstance.reset();
    expect(api.setPermissionOverride).not.toHaveBeenCalled();
    expect(api.resetPermissionOverrides).not.toHaveBeenCalled();
  });

  it('restablece todas las excepciones solo si existen', async () => {
    const fixture = open(); await flush(fixture);
    await fixture.componentInstance.reset();
    expect(api.resetPermissionOverrides).not.toHaveBeenCalled();
    api.effectivePermissions.mockResolvedValue(effective([{ id: 'b', granted: true, source: 'INDIVIDUAL', individualEffect: 'CONCEDER' }]));
    const second = open(); await flush(second);
    await second.componentInstance.reset();
    expect(api.resetPermissionOverrides).toHaveBeenCalledWith('u1');
  });

  it('muestra el error de la API sin perder la lista', async () => {
    api.setPermissionOverride.mockRejectedValue(new Error('fallo'));
    const fixture = open(); await flush(fixture);
    await fixture.componentInstance.toggle(fixture.componentInstance.rows()[0]);
    expect(fixture.componentInstance.error()).not.toBe('');
    expect(fixture.componentInstance.rows().length).toBe(3);
  });

  it('filtra por nombre o código', async () => {
    const fixture = open(); await flush(fixture);
    fixture.componentInstance.filter.set('sales.c');
    expect(fixture.componentInstance.groups().map(g => g.rows.map(r => r.id))).toEqual([['c']]);
  });
});

describe('Administración: plantilla de rol', () => {
  const api = { grantRolePermission: vi.fn(), revokeRolePermission: vi.fn(), resetRolePermissions: vi.fn() };
  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } }, { provide: AdminApi, useValue: api }] });
    confirmAction.mockResolvedValue(true);
  });
  function open() {
    const fixture = TestBed.createComponent(RoleTemplate);
    fixture.componentRef.setInput('role', role(['a']));
    fixture.componentRef.setInput('catalog', catalog);
    const updated = vi.fn(); fixture.componentInstance.updated.subscribe(updated);
    fixture.detectChanges();
    return { fixture, updated };
  }
  const box = (checked: boolean) => ({ checked }) as HTMLInputElement;

  it('marca los permisos de la plantilla y los agrupa por módulo', () => {
    const { fixture } = open();
    const root = fixture.nativeElement as HTMLElement;
    const boxes = Array.from(root.querySelectorAll<HTMLInputElement>('input[type=checkbox]'));
    expect(boxes.map(x => x.checked)).toEqual([true, false, false]);
    expect(Array.from(root.querySelectorAll('h4')).map(x => x.textContent)).toEqual(['CASH', 'SALES']);
  });

  it('concede y revoca permisos y emite el rol actualizado', async () => {
    const { fixture, updated } = open();
    api.grantRolePermission.mockResolvedValue(role(['a', 'b'])); api.revokeRolePermission.mockResolvedValue(role([]));
    await fixture.componentInstance.toggle(catalog[1], box(true));
    expect(api.grantRolePermission).toHaveBeenCalledWith('r1', 'b');
    await fixture.componentInstance.toggle(catalog[0], box(false));
    expect(api.revokeRolePermission).toHaveBeenCalledWith('r1', 'a');
    expect(updated).toHaveBeenCalledTimes(2);
  });

  it('revierte el checkbox si se cancela la confirmación o falla la API', async () => {
    const { fixture, updated } = open();
    confirmAction.mockResolvedValue(false);
    const cancelled = box(true);
    await fixture.componentInstance.toggle(catalog[1], cancelled);
    expect(cancelled.checked).toBe(false);
    expect(api.grantRolePermission).not.toHaveBeenCalled();
    confirmAction.mockResolvedValue(true);
    api.grantRolePermission.mockRejectedValue(new Error('fallo'));
    const failed = box(true);
    await fixture.componentInstance.toggle(catalog[1], failed);
    expect(failed.checked).toBe(false);
    expect(fixture.componentInstance.error()).not.toBe('');
    expect(updated).not.toHaveBeenCalled();
  });

  it('restaura la plantilla original solo tras confirmar', async () => {
    const { fixture, updated } = open();
    api.resetRolePermissions.mockResolvedValue(role(['a', 'b']));
    confirmAction.mockResolvedValue(false);
    await fixture.componentInstance.reset();
    expect(api.resetRolePermissions).not.toHaveBeenCalled();
    confirmAction.mockResolvedValue(true);
    await fixture.componentInstance.reset();
    expect(api.resetRolePermissions).toHaveBeenCalledWith('r1');
    expect(updated).toHaveBeenCalledOnce();
  });
});

describe('Administración: acceso a permisos desde la pantalla', () => {
  const api = { users: vi.fn(), roles: vi.fn(), permissionCatalog: vi.fn() };
  const state = { permissions: [] as string[] };
  const session = { has: (code: string) => state.permissions.includes(code), user: () => ({ id: 'admin' }) };
  let component: Admin;
  beforeEach(() => {
    vi.clearAllMocks();
    api.permissionCatalog.mockResolvedValue(catalog);
    api.roles.mockResolvedValue({ items: [role(['a'])], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } }, { provide: AdminApi, useValue: api }, { provide: SessionService, useValue: session }] });
    component = TestBed.runInInjectionContext(() => new Admin());
  });

  it('exige USER_PERMISSIONS.MANAGE y PERMISSIONS.READ para abrir excepciones', async () => {
    state.permissions = ['USERS.READ', 'USER_PERMISSIONS.MANAGE'];
    await component.openPermissions(user);
    expect(component.permissionUser()).toBeNull(); expect(api.permissionCatalog).not.toHaveBeenCalled();
    state.permissions = ['USERS.READ', 'USER_PERMISSIONS.MANAGE', 'PERMISSIONS.READ'];
    await component.openPermissions(user);
    expect(component.permissionUser()).toBe(user);
    await component.openPermissions(user);
    expect(api.permissionCatalog).toHaveBeenCalledOnce();
  });

  it('solo carga el catálogo para editar plantillas con ROLES.MANAGE y PERMISSIONS.READ', async () => {
    state.permissions = ['ROLES.READ'];
    await component.viewRole(role(['a']));
    expect(api.permissionCatalog).not.toHaveBeenCalled();
    component.viewedRole.set(null);
    state.permissions = ['ROLES.READ', 'ROLES.MANAGE', 'PERMISSIONS.READ'];
    await component.viewRole(role(['a']));
    expect(api.permissionCatalog).toHaveBeenCalledOnce();
    expect(component.canEditTemplates).toBe(true);
  });

  it('reemplaza el rol actualizado en la lista', async () => {
    state.permissions = ['ROLES.READ', 'ROLES.MANAGE', 'PERMISSIONS.READ'];
    await component.loadRoles();
    component.roleUpdated(role(['a', 'b']));
    expect(component.roles()[0].permissions.length).toBe(2);
    expect(component.viewedRole()?.permissions.length).toBe(2);
  });

  it('informa del fallo al leer el catálogo sin abrir el diálogo', async () => {
    state.permissions = ['USERS.READ', 'USER_PERMISSIONS.MANAGE', 'PERMISSIONS.READ'];
    api.permissionCatalog.mockRejectedValue(new Error('fallo'));
    await component.openPermissions(user);
    expect(component.permissionUser()).toBeNull();
    expect(component.error()).not.toBe('');
  });
});
