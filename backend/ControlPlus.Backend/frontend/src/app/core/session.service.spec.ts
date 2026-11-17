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
  afterEach(() => { http.verify(); service.logout(); sessionStorage.clear(); });

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
  describe('renovación mientras se usa', () => {
    const minute = 60_000;
    let base = 0;
    const user = (permissions: string[]) => ({ id: 'actor', userName: 'cajero', displayName: 'Cajero', roles: ['CAJERO'], permissions });
    const start = async () => {
      base = Date.now();
      const login = service.login('cajero', 'clave');
      http.expectOne('/api/auth/login').flush({ accessToken: 'token-1', expiresAtUtc: new Date(base + 30 * minute).toISOString(), user: user(['SALES.CREATE']) });
      await login;
    };

    it('renueva al pasar la mitad de la duración si hubo actividad, y guarda el token y los permisos nuevos', async () => {
      await start();
      service.touch(base + 2 * minute);
      await service.keepAlive(base + 5 * minute);
      http.expectNone('/api/auth/refresh');

      const renewal = service.keepAlive(base + 16 * minute);
      const request = http.expectOne('/api/auth/refresh');
      expect(request.request.method).toBe('POST');
      request.flush({ accessToken: 'token-2', expiresAtUtc: new Date(base + 46 * minute).toISOString(), user: user(['SALES.CREATE', 'CREDIT.REQUEST']) });
      await renewal;
      expect(service.token()).toBe('token-2');
      expect(service.has('CREDIT.REQUEST')).toBe(true);
      expect(JSON.parse(sessionStorage.getItem('controlplus.session')!).accessToken).toBe('token-2');
    });

    it('sin actividad desde el último token no renueva, y una sesión vencida tampoco', async () => {
      await start();
      await service.keepAlive(base + 20 * minute);
      service.touch(base + 25 * minute);
      await service.keepAlive(base + 31 * minute);
      http.expectNone('/api/auth/refresh');
      expect(service.token()).toBe('token-1');
    });

    it('dos renovaciones a la vez hacen una sola solicitud', async () => {
      await start();
      const first = service.renew(); const second = service.renew();
      http.expectOne('/api/auth/refresh').flush({ accessToken: 'token-2', expiresAtUtc: new Date(base + 60 * minute).toISOString(), user: user([]) });
      await Promise.all([first, second]);
      expect(service.token()).toBe('token-2');
    });

    it('una renovación que llega después de cerrar sesión no la reabre', async () => {
      await start();
      const renewal = service.renew();
      const request = http.expectOne('/api/auth/refresh');
      service.logout();
      request.flush({ accessToken: 'token-2', expiresAtUtc: new Date(base + 60 * minute).toISOString(), user: user([]) });
      await renewal;
      expect(service.token()).toBeNull();
      expect(sessionStorage.getItem('controlplus.session')).toBeNull();
    });

    it('si la renovación falla conserva la sesión actual hasta que venza', async () => {
      await start();
      const renewal = service.renew();
      http.expectOne('/api/auth/refresh').flush({ title: 'sin conexión' }, { status: 503, statusText: 'Service Unavailable' });
      await renewal;
      expect(service.token()).toBe('token-1');
    });
  });
});
