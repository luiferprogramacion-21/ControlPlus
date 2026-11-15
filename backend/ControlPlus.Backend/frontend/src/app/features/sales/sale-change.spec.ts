import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { ChangePreparation, SaleDetail, SalesApi } from './sales-api';
import { SaleChange } from './sale-change';

const sale: SaleDetail = { id: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '', operatingDate: '', grossSubtotal: 30000, discountTotal: 0, total: 30000, change: 0, state: 'CONFIRMADA', version: 4, creditId: null, payments: [],
  lines: [{ id: 'l1', productId: 'p1', productCode: 'P-1', productName: 'Camisa', measurementUnit: 'UND', quantity: 3, priceType: 'MINORISTA', unitPrice: 10000, discountAmount: 0, grossAmount: 30000, netAmount: 30000 }] };
const prep = (overrides: Partial<ChangePreparation> = {}): ChangePreparation => ({ saleId: 's1', visibleNumber: 'VTA-1', saleType: 'CONTADO', saleState: 'CONFIRMADA', saleVersion: 4, operatingDate: '', changeDeadline: '2026-11-20', currentLocalDate: '2026-11-06',
  withinDeadline: true, canStartChange: true, creditId: null, creditVersion: null, creditBalance: null,
  lines: [{ saleLineId: 'l1', productId: 'p1', productCode: 'P-1', productName: 'Camisa', soldQuantity: 3, returnedQuantity: 0, remainingQuantity: 3 }], ...overrides });
const pants = { id: 'p2', name: 'Pantalón', internalCode: 'P-2', retailPrice: 25000, wholesalePrice: null, availableStock: 5, isActive: true } as unknown as Product;

describe('SaleChange: cambio de productos', () => {
  const api = { changePreparation: vi.fn(), confirmChange: vi.fn(), pricingRules: vi.fn() };
  const catalog = { products: vi.fn() };
  const roles = { current: ['ADMINISTRADOR'] };
  const session = { user: () => ({ userName: 'admin' }), hasRole: (...c: string[]) => c.some(x => roles.current.includes(x)) };
  let prompt: AuthorizationPrompt | null; let dialogError: unknown;
  const dialog = { open: vi.fn(async (p: AuthorizationPrompt) => { prompt = p; dialogError = undefined; try { await p.run(roles.current.includes('ADMINISTRADOR') ? null : { userName: 'jefe', password: 'secreto' }); return true; } catch (e) { dialogError = e; return false; } }) };
  let changed: unknown[];

  async function open(p = prep()) {
    api.changePreparation.mockResolvedValue(p);
    const fixture = TestBed.createComponent(SaleChange);
    fixture.componentRef.setInput('sale', sale);
    changed = []; fixture.componentInstance.changed.subscribe(x => changed.push(x));
    fixture.detectChanges(); await fixture.whenStable();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    vi.clearAllMocks(); prompt = null; dialogError = undefined; roles.current = ['ADMINISTRADOR'];
    api.confirmChange.mockResolvedValue({ visibleNumber: 'CAM-1', difference: 5000 });
    api.pricingRules.mockResolvedValue({ wholesaleMinimumQuantity: 2 });
    TestBed.configureTestingModule({ providers: [{ provide: SalesApi, useValue: api }, { provide: CatalogApi, useValue: catalog }, { provide: AuthorizationDialog, useValue: dialog }, { provide: SessionService, useValue: session }] });
  });

  it('exige el mínimo mayorista configurado sobre las unidades entregadas', async () => {
    const c = await open(); expect(c.wholesaleMin()).toBe(2);
    c.returning()[0].quantity = 1; c.addDelivered({ ...pants, wholesalePrice: 20000 } as Product);
    c.delivering()[0].priceType = 'MAYORISTA';
    expect(c.validation()).toContain('exige entregar 2 o más unidades de Pantalón');
    c.delivering()[0].quantity = 2; expect(c.validation()).not.toContain('mayorista');
  });

  it('exige devolver y entregar al menos un producto', async () => {
    const c = await open();
    expect(c.validation()).toContain('devuelto');
    c.returning()[0].quantity = 1; expect(c.validation()).toContain('entregado');
  });

  it('calcula el valor devuelto, el entregado y la diferencia', async () => {
    const c = await open(); c.returning()[0].quantity = 1; c.addDelivered(pants);
    expect(c.totalReturned).toBe(10000); expect(c.totalDelivered).toBe(25000); expect(c.difference).toBe(15000);
    expect(c.settlement).toMatchObject({ mode: 'payment', cashAmount: 15000, treatment: 'AUTO' });
  });

  it('rechaza devolver más de lo disponible y vencido el plazo', async () => {
    const c = await open(); c.returning()[0].quantity = 4; c.addDelivered(pants);
    expect(c.validation()).toContain('hasta 3');
    const late = await open(prep({ canStartChange: false, withinDeadline: false })); expect(late.validation()).toContain('venció');
  });

  it('exige que los medios sumen exactamente la diferencia', async () => {
    const c = await open(); c.returning()[0].quantity = 1; c.addDelivered(pants);
    c.drafts[0].appliedAmount = 10000; c.drafts[0].receivedAmount = 10000;
    expect(c.validation()).toContain('sumar exactamente');
    c.drafts[0].appliedAmount = 15000; c.drafts[0].receivedAmount = 20000; expect(c.validation()).toBe('');
  });

  it('el Administrador confirma sin ventana y sin authorization, con la versión de la preparación', async () => {
    const c = await open(); c.returning()[0].quantity = 1; c.addDelivered(pants);
    c.drafts[0].appliedAmount = 15000; c.drafts[0].receivedAmount = 20000;
    await c.confirm();
    expect(prompt?.selfAuthorizedRoles).toEqual(['ADMINISTRADOR']);
    const [id, body, key] = api.confirmChange.mock.calls[0];
    expect(id).toBe('s1'); expect(key).toBeTruthy();
    expect(body).toEqual({ expectedSaleVersion: 4, expectedCreditVersion: null, treatment: 'AUTO',
      returned: [{ saleLineId: 'l1', quantity: 1, resellable: true }], delivered: [{ productId: 'p2', quantity: 1, priceType: 'MINORISTA' }],
      payments: [{ methodCode: 'EFECTIVO', amount: 15000, receivedAmount: 20000, reference: null }] });
    expect(changed).toHaveLength(1);
  });

  it('una devolución al cliente envía receivedAmount nulo', async () => {
    const c = await open(); c.returning()[0].quantity = 2; c.addDelivered({ ...pants, retailPrice: 5000 });
    expect(c.settlement).toMatchObject({ mode: 'refund', cashAmount: 15000 });
    c.drafts[0].appliedAmount = 15000;
    await c.confirm();
    expect(api.confirmChange.mock.calls[0][1].payments).toEqual([{ methodCode: 'EFECTIVO', amount: 15000, receivedAmount: null, reference: null }]);
  });

  it('crédito con diferencia positiva exige elegir y AGREGAR_CREDITO no envía pagos', async () => {
    const c = await open(prep({ saleType: 'CREDITO', creditId: 'c1', creditVersion: 7, creditBalance: 20000 }));
    c.returning()[0].quantity = 1; c.addDelivered(pants);
    expect(c.validation()).toContain('Elige');
    c.choice = 'AGREGAR_CREDITO'; expect(c.validation()).toBe('');
    await c.confirm();
    expect(api.confirmChange.mock.calls[0][1]).toMatchObject({ expectedCreditVersion: 7, treatment: 'AGREGAR_CREDITO', payments: [] });
  });

  it('el Supervisor envía credenciales y un 403 explica el rol superior', async () => {
    roles.current = ['SUPERVISOR']; api.confirmChange.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    const c = await open(); c.returning()[0].quantity = 1; c.addDelivered(pants); c.drafts[0].appliedAmount = 15000; c.drafts[0].receivedAmount = 15000;
    await c.confirm();
    expect(api.confirmChange.mock.calls[0][1].authorization).toEqual({ userName: 'jefe', password: 'secreto' });
    expect((dialogError as Error).message).toContain('rol superior');
  });

  it('reintentar el mismo cambio conserva la clave de idempotencia', async () => {
    api.confirmChange.mockRejectedValueOnce(new HttpErrorResponse({ status: 500 }));
    const c = await open(); c.returning()[0].quantity = 1; c.addDelivered(pants); c.drafts[0].appliedAmount = 15000; c.drafts[0].receivedAmount = 15000;
    await c.confirm(); await c.confirm();
    expect(api.confirmChange.mock.calls[0][2]).toBe(api.confirmChange.mock.calls[1][2]);
  });
});
