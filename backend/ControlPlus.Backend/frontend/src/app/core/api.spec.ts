import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor, authorizationContext, errorText, TECHNICAL_FALLBACK } from './api';
import { SessionService } from './session.service';

describe('Intercepción de autorización', () => {
  it('expone el título seguro de conflicto y los errores de validación', () => {
    expect(errorText(new HttpErrorResponse({ status: 409, error: { title: 'La clave de idempotencia ya fue utilizada con otra solicitud.' } }))).toContain('clave de idempotencia');
    expect(errorText(new HttpErrorResponse({ status: 400, error: { errors: { request: ['Costo requerido.'] } } }))).toBe('Costo requerido.');
  });
  it('un rechazo de credenciales del autorizador conserva la sesión del operador', async () => {
    const session = { token: () => 'token-operador', logout: vi.fn(), touch: vi.fn() };
    const router = { navigateByUrl: vi.fn() };
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
      { provide: SessionService, useValue: session }, { provide: Router, useValue: router },
    ] });
    const http = TestBed.inject(HttpTestingController);
    const task = firstValueFrom(TestBed.inject(HttpClient).post('/api/inventory/adjustments', { authorization: { userName: 'otro', password: 'incorrecta' } }, { context: authorizationContext() }));
    const request = http.expectOne('/api/inventory/adjustments');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-operador');
    request.flush({ title: 'Credenciales inválidas', code: 'authentication.unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await expect(task).rejects.toBeTruthy();
    expect(session.logout).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    http.verify();
  });
  it('las consultas a la API cuentan como actividad de la sesión, la renovación no', async () => {
    const session = { token: () => 'token-operador', logout: vi.fn(), touch: vi.fn() };
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
      { provide: SessionService, useValue: session }, { provide: Router, useValue: { navigateByUrl: vi.fn() } },
    ] });
    const http = TestBed.inject(HttpTestingController);
    const client = TestBed.inject(HttpClient);
    const search = firstValueFrom(client.get('/api/products'));
    http.expectOne('/api/products').flush({});
    await search;
    expect(session.touch).toHaveBeenCalledTimes(1);
    const renewal = firstValueFrom(client.post('/api/auth/refresh', null));
    http.expectOne('/api/auth/refresh').flush({});
    await renewal;
    expect(session.touch).toHaveBeenCalledTimes(1);
    http.verify();
  });
});

describe('Mensajes técnicos del servidor', () => {
  it('no muestra al usuario mensajes escritos para programadores', () => {
    const technical = new HttpErrorResponse({ status: 400, error: { title: 'purchaseId debe ser un UUID válido.' } });
    expect(errorText(technical)).toBe(TECHNICAL_FALLBACK);
    const plain = new HttpErrorResponse({ status: 409, error: { title: 'La venta ya fue anulada.' } });
    expect(errorText(plain)).toBe('La venta ya fue anulada.');
  });
});
