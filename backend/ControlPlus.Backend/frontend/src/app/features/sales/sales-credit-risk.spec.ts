import { TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { CustomerCreditRisk, SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

const product = { id: '019a0000-0000-7000-8000-000000000001', isActive: true, availableStock: 5, retailPrice: 10000, wholesalePrice: null } as any;
const customer = { id: '019a0000-0000-7000-8000-000000000002', fullName: 'Ana', isActive: true, phone: '3001234567', documentType: null, documentNumber: null } as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false, currentShift: { operatingDate: '2026-10-24', responsibleUserId: '019a0000-0000-7000-8000-000000000003' } } as any;
const risk = (over: Partial<CustomerCreditRisk> = {}): CustomerCreditRisk => ({
  customerId: customer.id, outstandingBalance: 60000, overdueBalance: 25000, overdueCredits: 1, creditLimit: null, availableCredit: null,
  overdue: true, exceedsLimit: false, blockRiskyCredit: false, blocked: false, ...over,
});

describe('Sales: clientes morosos y cupo de crédito', () => {
  let component: Sales;
  const creditCheck = vi.fn();
  const open = vi.fn();
  const loaded = async () => { for (let i = 0; i < 4; i++) await Promise.resolve(); };
  const create = async () => {
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); component.cashState.set(cash); await loaded();
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
  };
  beforeEach(() => {
    vi.clearAllMocks();
    creditCheck.mockResolvedValue(risk());
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ modalidad: 'credito' }) } } },
      { provide: SalesApi, useValue: { confirm: vi.fn(), creditRules: vi.fn().mockResolvedValue({ defaultDays: 30, blockRiskyCredit: false }), creditCheck } },
      { provide: CashApi, useValue: { state: vi.fn().mockResolvedValue(cash) } }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: { open } },
      { provide: SessionService, useValue: { has: (permission: string) => ['SALES.CREATE', 'CREDIT.REQUEST'].includes(permission), hasRole: (...codes: string[]) => codes.includes('SUPERVISOR'), user: () => ({ id: '019a0000-0000-7000-8000-000000000003', userName: 'super' }) } },
    ] });
  });

  it('al ir al cobro consulta al cliente con el total y avisa del saldo vencido', async () => {
    await create();
    component.startCheckout(); await loaded();
    expect(creditCheck).toHaveBeenCalledWith(customer.id, 10000);
    expect(component.creditRisk()?.overdue).toBe(true);
    expect(component.creditRiskText).toContain('vencidos en 1 crédito');
  });

  it('describe el cupo superado con lo disponible', async () => {
    creditCheck.mockResolvedValue(risk({ overdue: false, overdueBalance: 0, overdueCredits: 0, exceedsLimit: true, creditLimit: 65000, availableCredit: 5000 }));
    await create(); component.startCheckout(); await loaded();
    expect(component.creditRiskText).toContain('supera su cupo');
    expect(component.creditRiskText).not.toContain('vencidos');
  });

  it('un cliente al día no muestra aviso', async () => {
    creditCheck.mockResolvedValue(risk({ overdue: false, overdueBalance: 0, overdueCredits: 0 }));
    await create(); component.startCheckout(); await loaded();
    expect(component.creditRisk()).toBeNull();
  });

  it('en modo aviso el Supervisor sigue autorizándose a sí mismo', async () => {
    await create(); component.startCheckout(); await loaded();
    await component.confirm();
    // Solo cuenta el rol de quien vende (aquí un Supervisor): un Cajero siempre pasa por la ventana de autorización.
    expect(open).toHaveBeenCalledWith(expect.objectContaining({ title: 'Autorizar venta a crédito', selfAuthorizedRoles: ['SUPERVISOR'] }));
  });

  it('con el bloqueo activo solo un Administrador autoriza o vende la excepción', async () => {
    creditCheck.mockResolvedValue(risk({ blockRiskyCredit: true, blocked: true }));
    await create(); component.startCheckout(); await loaded();
    await component.confirm();
    const prompt = open.mock.calls[0][0];
    expect(prompt.title).toBe('Autorizar crédito bloqueado');
    // Un Supervisor no es Administrador: con el bloqueo activo necesita la autorización de uno.
    expect(prompt.selfAuthorizedRoles).toEqual([]);
    expect(prompt.description).toContain('solo un Administrador');
  });

  it('si la consulta falla no bloquea la pantalla: decide el servidor', async () => {
    creditCheck.mockRejectedValue(new Error('sin respuesta'));
    await create(); component.startCheckout(); await loaded();
    expect(component.creditRisk()).toBeNull();
    expect(component.checkoutMode()).toBe(true);
  });
});
