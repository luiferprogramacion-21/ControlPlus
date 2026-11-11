import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';
import { SalePrinting } from './sale-printing';
import { SaleDetail, SalesApi } from './sales-api';
import { SalesHistory } from './sales-history';

const sale = (state = 'CONFIRMADA'): SaleDetail => ({ id: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '', operatingDate: '', grossSubtotal: 1000, discountTotal: 0, total: 1000, change: 0, state, version: 1, creditId: null, lines: [], payments: [] });

describe('SalesHistory: reimpresión del ticket', () => {
  const api = { list: vi.fn(), detail: vi.fn() };
  const printing = { reprint: vi.fn(), lastStatus: vi.fn() };
  let permissions: string[];
  const session = { user: () => ({ userName: 'admin' }), has: (code: string) => permissions.includes(code), hasRole: () => true };
  async function create() {
    const fixture = TestBed.createComponent(SalesHistory); fixture.detectChanges(); await fixture.whenStable(); return fixture.componentInstance;
  }
  beforeEach(() => {
    vi.clearAllMocks(); permissions = ['SALES.OWN_READ', 'SALES.REPRINT'];
    api.list.mockResolvedValue({ items: [{ id: 's1', visibleNumber: 'VTA-1' }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.detail.mockResolvedValue(sale()); printing.lastStatus.mockResolvedValue('IMPRESO');
    printing.reprint.mockResolvedValue({ message: 'Reimpresión: PENDIENTE.', status: 'PENDIENTE' });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SalesApi, useValue: api }, { provide: SalePrinting, useValue: printing }, { provide: AuthorizationDialog, useValue: { open: vi.fn() } }, { provide: SessionService, useValue: session }] });
  });

  it('solo se puede reimprimir una venta confirmada con SALES.REPRINT', async () => {
    const c = await create();
    expect(c.canReprint(sale())).toBe(true); expect(c.canReprint(sale('ANULADA'))).toBe(false);
    permissions = ['SALES.OWN_READ']; expect(c.canReprint(sale())).toBe(false);
  });
  it('al abrir el detalle muestra el estado del último ticket', async () => {
    const c = await create(); await c.open({ id: 's1' } as never);
    expect(c.printStatus()).toBe('IMPRESO');
  });
  it('reimprimir muestra el resultado y actualiza el estado', async () => {
    const c = await create(); await c.open({ id: 's1' } as never); await c.reprint();
    expect(printing.reprint).toHaveBeenCalledWith('s1'); expect(c.printMessage()).toContain('PENDIENTE'); expect(c.printStatus()).toBe('PENDIENTE'); expect(c.reprinting()).toBe(false);
  });
});
