import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminApi, AdminUser } from './admin-api';

describe('AdminApi: usuarios y roles', () => {
  let api: AdminApi;
  let http: HttpTestingController;
  const user = { id: 'u1', userName: 'ana', displayName: 'Ana', isActive: true, isLocked: false, failedLoginAttempts: 0,
    lockedAtUtc: null, lastLoginAtUtc: null, roles: [] } as AdminUser;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AdminApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('consulta el carné sin reemitirlo y lo elimina con identidad y versión', async () => {
    const read = api.currentCredential('u1');
    const get = http.expectOne('/api/cash/operator-credentials/u1');
    expect(get.request.method).toBe('GET'); get.flush(null); await read;
    const remove = api.revokeCredential('u1', 'c1', 4);
    const request = http.expectOne(r => r.url === '/api/cash/operator-credentials/u1');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.params.get('credentialId')).toBe('c1');
    expect(request.request.params.get('expectedVersion')).toBe('4');
    request.flush(null); await remove;
  });

  it('lista usuarios con filtros y omite los parámetros vacíos', async () => {
    const task = api.users({ search: 'ana', isActive: true, isLocked: null, page: 2, pageSize: 20 });
    const request = http.expectOne(r => r.url === '/api/users');
    expect(request.request.params.get('search')).toBe('ana');
    expect(request.request.params.get('isActive')).toBe('true');
    expect(request.request.params.has('isLocked')).toBe(false);
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 });
    await task;
  });

  it('crea un usuario con exactamente los campos del contrato', async () => {
    const task = api.createUser({ userName: 'ana', displayName: 'Ana', password: 'x', roleIds: ['r1'], email: 'ana@example.com' });
    const request = http.expectOne('/api/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userName: 'ana', displayName: 'Ana', password: 'x', roleIds: ['r1'], email: 'ana@example.com' });
    request.flush(user); await task;
  });

  it('edita el nombre, asigna el rol y reactiva con las rutas del contrato', async () => {
    const edit = api.updateUser('u1', 'Ana María', null);
    const put = http.expectOne('/api/users/u1');
    expect(put.request.method).toBe('PUT'); expect(put.request.body).toEqual({ displayName: 'Ana María', email: null });
    put.flush(user); await edit;
    const role = api.assignRole('u1', 'r2');
    const post = http.expectOne('/api/users/u1/roles');
    expect(post.request.method).toBe('POST'); expect(post.request.body).toEqual({ roleId: 'r2' });
    post.flush(user); await role;
    const unlock = api.reactivateUser('u1');
    const reactivate = http.expectOne('/api/users/u1/reactivate');
    expect(reactivate.request.method).toBe('POST');
    reactivate.flush(user); await unlock;
  });

  it('activa o inactiva según el estado vigente', async () => {
    const off = api.setUserActive(user);
    const deactivate = http.expectOne('/api/users/u1/deactivate');
    expect(deactivate.request.method).toBe('PUT');
    deactivate.flush(user); await off;
    const on = api.setUserActive({ ...user, isActive: false });
    const activate = http.expectOne('/api/users/u1/activate');
    expect(activate.request.method).toBe('PUT');
    activate.flush(user); await on;
  });

  it('cambia la contraseña con currentPassword y newPassword', async () => {
    const task = api.changePassword('u1', 'nueva', 'actual');
    const request = http.expectOne('/api/users/u1/password');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ currentPassword: 'actual', newPassword: 'nueva' });
    request.flush(null); await task;
  });

  it('consulta los roles con sus permisos', async () => {
    const task = api.roles({ page: 1, pageSize: 50 });
    const request = http.expectOne(r => r.url === '/api/roles');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    await task;
  });

  it('consulta y modifica excepciones individuales con las rutas del contrato', async () => {
    const effective = api.effectivePermissions('u1');
    http.expectOne('/api/users/u1/permissions/effective').flush([{ id: 'p1', code: 'SALES.CREATE', name: 'Vender', granted: true, source: 'ROL', individualEffect: null }]);
    expect((await effective)[0].source).toBe('ROL');
    const grant = api.setPermissionOverride('u1', 'p1', false);
    const put = http.expectOne('/api/users/u1/permissions/p1');
    expect(put.request.method).toBe('PUT'); expect(put.request.body).toEqual({ granted: false });
    put.flush(null); await grant;
    const reset = api.resetPermissionOverrides('u1');
    const del = http.expectOne('/api/users/u1/permissions');
    expect(del.request.method).toBe('DELETE');
    del.flush(null); await reset;
  });

  it('edita la plantilla de un rol: conceder, revocar y restaurar', async () => {
    const role = { id: 'r1', code: 'CAJERO', name: 'Cajero', level: 1, isActive: true, permissions: [] };
    const grant = api.grantRolePermission('r1', 'p1');
    const post = http.expectOne('/api/roles/r1/permissions/p1');
    expect(post.request.method).toBe('POST'); post.flush(role); await grant;
    const revoke = api.revokeRolePermission('r1', 'p1');
    const del = http.expectOne('/api/roles/r1/permissions/p1');
    expect(del.request.method).toBe('DELETE'); del.flush(role); await revoke;
    const reset = api.resetRolePermissions('r1');
    const restore = http.expectOne('/api/roles/r1/permissions/reset');
    expect(restore.request.method).toBe('POST'); restore.flush(role); await reset;
  });

  it('lee todo el catálogo de permisos activos en páginas de 200', async () => {
    const permission = (id: string) => ({ id, code: id, name: id, description: null, module: 'M', isActive: true });
    const task = api.permissionCatalog();
    const first = http.expectOne(r => r.url === '/api/permissions' && r.params.get('page') === '1');
    expect(first.request.params.get('pageSize')).toBe('200');
    expect(first.request.params.get('isActive')).toBe('true');
    first.flush({ items: [permission('a')], page: 1, pageSize: 200, totalCount: 2, totalPages: 2 });
    let second!: ReturnType<typeof http.expectOne>;
    await vi.waitFor(() => { second = http.expectOne(r => r.url === '/api/permissions' && r.params.get('page') === '2'); });
    second.flush({ items: [permission('b')], page: 2, pageSize: 200, totalCount: 2, totalPages: 2 });
    expect((await task).map(x => x.id)).toEqual(['a', 'b']);
  });
});
