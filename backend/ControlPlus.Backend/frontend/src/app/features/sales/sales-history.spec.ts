import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { SaleDetail, SalesApi } from './sales-api';
import { SalesHistory } from './sales-history';

const sale = (state = 'CONFIRMADA', version = 3): SaleDetail => ({ id: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '', operatingDate: '', grossSubtotal: 10000, discountTotal: 0, total: 10000, change: 0, state, version, creditId: null, lines: [], payments: [] });
const reasons = [{ id: 'r1', code: 'ERROR_REGISTRO', name: 'Error de registro', displayOrder: 1 }, { id: 'r2', code: 'OTRO', name: 'Otro', displayOrder: 3 }];

describe('SalesHistory: historial y anulación', () => {
  const api = { list: vi.fn(), detail: vi.fn(), cancellationReasons: vi.fn(), cancel: vi.fn(), printJobs: vi.fn(), requestReprint: vi.fn(), retryPrintJob: vi.fn() };
  let permissions: string[]; let roles: string[];
  const session = { user: () => ({ userName: 'admin' }), has: (code: string) => permissions.includes(code), hasRole: (...codes: string[]) => codes.some(code => roles.includes(code)) };
  let prompt: AuthorizationPrompt | null; let dialogError: unknown;
  const dialog = { open: vi.fn(async (p: AuthorizationPrompt) => { prompt = p; dialogError = undefined; try { await p.run(roles.includes('ADMINISTRADOR') ? null : { userName: 'jefe', password: 'secreto' }); return true; } catch (e) { dialogError = e; return false; } }) };

  async function create(detail = sale()) {
    const fixture = TestBed.createComponent(SalesHistory);
    fixture.detectChanges(); await fixture.whenStable();
    fixture.componentInstance.detail.set(detail);
    return fixture.componentInstance;
  }

  beforeEach(() => {
    vi.clearAllMocks(); prompt = null; dialogError = undefined; permissions = ['SALES.OWN_READ', 'SALES.VOID']; roles = ['ADMINISTRADOR'];
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.cancellationReasons.mockResolvedValue(reasons);
    api.cancel.mockResolvedValue({ visibleNumber: 'VTA-1', reversedAmount: 10000 });
    api.printJobs.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.requestReprint.mockResolvedValue({ requestId: 'r', saleId: 's1', requestedAtUtc: '', status: 'SOLICITADA' });
    api.retryPrintJob.mockResolvedValue({});
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SalesApi, useValue: api }, { provide: AuthorizationDialog, useValue: dialog }, { provide: SessionService, useValue: session }] });
  });

  it('lista las ventas propias o todas según el permiso', async () => {
    await create(); expect(api.list).toHaveBeenLastCalledWith(false, 1, 20);
    permissions.push('SALES.ALL_READ'); await create(); expect(api.list).toHaveBeenLastCalledWith(true, 1, 20);
  });

  it('muestra el detalle en una ventana modal y permite cerrarla', async () => {
    const fixture = TestBed.createComponent(SalesHistory);
    fixture.detectChanges(); await fixture.whenStable();
    fixture.componentInstance.detail.set(sale()); fixture.detectChanges();
    const dialog = fixture.nativeElement.querySelector('[role="dialog"][aria-labelledby="sale-detail-title"]');
    expect(dialog).toBeTruthy();
    expect(dialog.querySelector('.sales-history-detail')).toBeTruthy();
    expect(dialog.querySelector('#sale-detail-title')?.textContent).toContain('VTA-1');
    expect(dialog.querySelector('table')).toBeNull();
    expect(dialog.querySelector('.sale-detail-actions .sale-detail-close')?.textContent).toContain('Cerrar');
    dialog.querySelector('.module-form-sheet__backdrop').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#sale-detail-title')).toBeNull();
  });

  it.each([['ADMINISTRADOR', true], ['SUPERVISOR', true], ['CAJERO', false]])('%s: botón de anular = %s', async (role, visible) => {
    roles = [role]; const component = await create();
    expect(component.canCancel(sale())).toBe(visible);
  });

  it('no permite anular sin el permiso o si la venta ya está anulada', async () => {
    const component = await create(sale('ANULADA')); expect(component.canCancel(sale('ANULADA'))).toBe(false);
    permissions = ['SALES.OWN_READ']; expect(component.canCancel(sale())).toBe(false);
  });

  it('exige motivo y observación cuando el motivo es OTRO', async () => {
    const component = await create(); await component.startCancel();
    expect(component.validation()).toContain('motivo');
    component.reasonId = 'r2'; expect(component.validation()).toContain('observación');
    component.observation = 'Cliente se arrepintió'; expect(component.validation()).toBe('');
  });

  it('el Administrador anula sin ventana de credenciales y sin authorization', async () => {
    const component = await create(); await component.startCancel(); component.reasonId = 'r1';
    await component.confirm();
    expect(prompt?.selfAuthorizedRoles).toEqual(['ADMINISTRADOR']);
    const [id, body, key] = api.cancel.mock.calls[0];
    expect(id).toBe('s1'); expect(body).toEqual({ expectedVersion: 3, reasonId: 'r1', observation: null }); expect(key).toBeTruthy();
    expect(component.notice()).toContain('VTA-1');
  });

  it('el Supervisor envía las credenciales del autorizador', async () => {
    roles = ['SUPERVISOR']; const component = await create(); await component.startCancel(); component.reasonId = 'r1';
    await component.confirm();
    expect(api.cancel.mock.calls[0][1].authorization).toEqual({ userName: 'jefe', password: 'secreto' });
  });

  it('un 403 explica que el autorizador debe tener un rol superior y reinicia la clave', async () => {
    roles = ['SUPERVISOR']; api.cancel.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    const component = await create(); await component.startCancel(); component.reasonId = 'r1';
    await component.confirm();
    expect((dialogError as Error).message).toContain('rol superior');
  });

  it('reintentar con el mismo cuerpo conserva la clave de idempotencia', async () => {
    api.cancel.mockRejectedValueOnce(new HttpErrorResponse({ status: 500 }));
    const component = await create(); await component.startCancel(); component.reasonId = 'r1';
    await component.confirm(); await component.confirm();
    expect(api.cancel.mock.calls[0][2]).toBe(api.cancel.mock.calls[1][2]);
  });

  describe('impresión', () => {
    const summary = { id: 's1', visibleNumber: 'VTA-1', state: 'CONFIRMADA' } as never;
    const job = (status: string, extra = {}) => ({ id: 'j1', saleId: 's1', copyType: 'REIMPRESION', copyNumber: 1, status, attempts: 1, requestedAtUtc: '2026-11-06T00:00:00Z', queueAcceptedAtUtc: null, error: null, ...extra });
    it('Imprimir exige SALES.REPRINT y venta confirmada', async () => {
      const component = await create(); expect(component.canPrint(summary)).toBe(false);
      permissions.push('SALES.REPRINT'); expect(component.canPrint(summary)).toBe(true); expect(component.canPrint({ id: 's1', state: 'ANULADA' } as never)).toBe(false);
    });
    it('reutiliza la clave si falla y la renueva tras un éxito', async () => {
      const component = await create(); api.requestReprint.mockRejectedValueOnce(new HttpErrorResponse({ status: 500 }));
      await component.print(summary); await component.print(summary); await component.print(summary);
      const keys = api.requestReprint.mock.calls.map(call => call[1]);
      expect(keys[0]).toBe(keys[1]); expect(keys[2]).not.toBe(keys[1]);
    });
    it('IMPRESO se informa como aceptado por la cola, no como salida en papel', async () => {
      const component = await create(); api.printJobs.mockResolvedValue({ items: [job('IMPRESO')], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
      await component.print(summary);
      expect(component.notice()).toContain('Aceptado por la cola de Windows'); expect(component.notice()).toContain('no confirma');
    });
    it('sin trabajo en cola avisa que falta un perfil', async () => {
      const component = await create(); await component.print(summary); expect(component.notice()).toContain('perfil de impresora');
    });
  });
});
