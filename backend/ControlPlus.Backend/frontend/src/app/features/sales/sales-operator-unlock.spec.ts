import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

const shared = (session: unknown = null) => ({ isConfigured: true, hasOpenShift: true, requiresPreviousShiftClosure: false,
  requiresOperatorSession: true, ownOperatorSession: session, currentShift: null }) as any;

describe('Sales: turno compartido, desbloqueo con carné (maqueta 06b)', () => {
  let component: Sales;
  const cash = { state: vi.fn(), startSession: vi.fn() };
  const flush = async () => { for (let i = 0; i < 6; i++) await Promise.resolve(); };
  beforeEach(async () => {
    vi.clearAllMocks();
    cash.state.mockResolvedValue(shared());
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: SalesApi, useValue: { heldSales: vi.fn().mockResolvedValue([]), pricingRules: vi.fn().mockResolvedValue({ wholesaleMinimumQuantity: 12 }) } },
      { provide: CashApi, useValue: cash }, { provide: CatalogApi, useValue: {} }, { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: SessionService, useValue: { has: (code: string) => code === 'SALES.CREATE', hasRole: () => false, user: () => ({ id: 'u1', userName: 'cajera', displayName: 'Ana' }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); await flush();
  });

  it('sin sesión de operador el punto de venta queda bloqueado', () => {
    expect(component.needsOperatorUnlock).toBe(true);
    cash.state.mockResolvedValue(shared({ id: 's1', state: 'ACTIVA' }));
  });

  it('el carné válido abre la sesión y desbloquea', async () => {
    cash.startSession.mockResolvedValue({ id: 's1', state: 'ACTIVA' });
    cash.state.mockResolvedValue(shared({ id: 's1', state: 'ACTIVA' }));
    component.operatorCredential = ' CP-123 ';
    await component.unlock();
    expect(cash.startSession).toHaveBeenCalledWith('CP-123');
    expect(component.needsOperatorUnlock).toBe(false);
    expect(component.operatorCredential).toBe('');
  });

  it('un carné inválido lo explica y no desbloquea', async () => {
    cash.startSession.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { code: 'authentication.unauthorized' } }));
    component.operatorCredential = 'mal';
    await component.unlock();
    expect(component.unlockError()).toContain('El carné no es válido');
    expect(component.needsOperatorUnlock).toBe(true);
    expect(component.operatorCredential).toBe('');
  });

  it('en turno individual no pide carné', async () => {
    cash.state.mockResolvedValue({ ...shared(), requiresOperatorSession: false });
    await component.refreshCash();
    expect(component.needsOperatorUnlock).toBe(false);
  });
});
