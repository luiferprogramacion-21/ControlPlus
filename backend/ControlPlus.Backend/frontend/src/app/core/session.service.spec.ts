import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from './session.service';

describe('SessionService', () => {
  let service: SessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  it('inicia, revalida y cierra una sesión real de API', async () => {
    const login = service.login('operador', 'clave');
    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userName: 'operador', password: 'clave' });
    request.flush({
      accessToken: 'token-de-prueba',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      user: { id: '1', userName: 'operador', displayName: 'Operador', roles: ['Cajero'], permissions: ['PRODUCTS.READ'] },
    });
    await login;
    expect(service.has('PRODUCTS.READ')).toBe(true);
    expect(service.has('CATEGORIES.MANAGE')).toBe(false);

    const validation = service.validate();
    const me = http.expectOne('/api/auth/me');
    expect(me.request.method).toBe('GET');
    me.flush({ id: '1', userName: 'operador', displayName: 'Operador', roles: ['Cajero'], permissions: [] });
    expect(await validation).toBe(true);
    expect(service.has('PRODUCTS.READ')).toBe(false);

    service.logout();
    expect(service.token()).toBeNull();
    expect(sessionStorage.getItem('controlplus.session')).toBeNull();
  });

  it('usa códigos de rol de /me y actualiza permisos efectivos sin cambiar la identidad', async () => {
    const login = service.login('operador', 'clave');
    http.expectOne('/api/auth/login').flush({ accessToken: 'token', expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      user: { id: 'actor', userName: 'operador', displayName: 'Nombre visible', roles: [' SUPERVISOR '], permissions: ['PRODUCTS.CREATE'] } });
    await login;
    expect(service.hasRole('SUPERVISOR')).toBe(true);
    expect(service.hasRole('ADMINISTRADOR')).toBe(false);
    const refreshed = service.refreshUser();
    http.expectOne('/api/auth/me').flush({ id: 'actor', userName: 'operador', displayName: 'Nombre visible', roles: ['ADMINISTRADOR'], permissions: [] });
    await refreshed;
    expect(service.hasRole('ADMINISTRADOR')).toBe(true);
    expect(service.has('PRODUCTS.CREATE')).toBe(false);
    expect(service.user()?.id).toBe('actor');
    expect(service.token()).toBe('token');
  });
});
