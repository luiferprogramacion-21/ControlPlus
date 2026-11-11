import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { errorText } from '../../core/api';
import { ReportsApi, salesPdfName } from './reports-api';

describe('ReportsApi', () => {
  let api: ReportsApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(ReportsApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());

  it('pide el resumen de ventas con from y to', async () => {
    const task = api.salesOverview('2026-11-01', '2026-11-07');
    const req = http.expectOne(r => r.url === '/api/reports/sales/overview');
    expect(req.request.method).toBe('GET'); expect(req.request.params.get('from')).toBe('2026-11-01'); expect(req.request.params.get('to')).toBe('2026-11-07');
    req.flush({ currency: 'COP' }); expect((await task).currency).toBe('COP');
  });

  it('pide el flujo de caja diario con from y to', async () => {
    const task = api.cashFlow('2026-11-01', '2026-11-02');
    const req = http.expectOne(r => r.url === '/api/reports/cash-flow/daily');
    expect(req.request.params.get('from')).toBe('2026-11-01'); expect(req.request.params.get('to')).toBe('2026-11-02');
    req.flush({ days: [] }); await task;
  });

  it('descarga el PDF como blob con el nombre acordado', async () => {
    const task = api.salesPdf('2026-11-01', '2026-11-07');
    const req = http.expectOne(r => r.url === '/api/reports/sales/overview.pdf');
    expect(req.request.responseType).toBe('blob'); expect(req.request.params.get('from')).toBe('2026-11-01');
    req.flush(new Blob(['%PDF-'], { type: 'application/pdf' }));
    const file = await task;
    expect(file.fileName).toBe('reporte-ventas-2026-11-01_2026-11-07.pdf'); expect(file.fileName).toBe(salesPdfName('2026-11-01', '2026-11-07')); expect(file.blob.size).toBe(5);
  });

  it('un error del PDF conserva el mensaje de validación del servidor aunque llegue como blob', async () => {
    const task = api.salesPdf('2026-11-01', '2026-12-31').catch(e => e);
    http.expectOne(r => r.url === '/api/reports/sales/overview.pdf')
      .flush(new Blob([JSON.stringify({ code: 'validation.failed', errors: { request: ['El rango admite hasta 31 días.'] } })], { type: 'application/problem+json' }), { status: 400, statusText: 'Bad Request' });
    const error = await task;
    expect(error).toBeInstanceOf(HttpErrorResponse); expect(error.status).toBe(400);
    expect(errorText(error)).toBe('El rango admite hasta 31 días.');
  });

  it('un 403 del PDF no entrega archivo ni detalles técnicos', async () => {
    const task = api.salesPdf('2026-11-01', '2026-11-07').catch(e => e);
    http.expectOne(r => r.url === '/api/reports/sales/overview.pdf').flush(new Blob(['no json']), { status: 403, statusText: 'Forbidden' });
    const error = await task;
    expect(error.status).toBe(403); expect(errorText(error)).toBe('No tienes permiso para esta operación.');
  });
});
