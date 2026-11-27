import { suggestMatches } from '../../shared/suggest-field';
import { TestBed } from '@angular/core/testing';
import { currentColombiaDate, Purchases } from './purchases';
import { PurchasesApi } from './purchases-api';
import { OrdersApi } from './orders-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

describe('Purchases', () => {
  let component: Purchases;
  let receive: ReturnType<typeof vi.fn>;
  let getOrder: ReturnType<typeof vi.fn>;
  let listOrders: ReturnType<typeof vi.fn>;
  const orderId = '019a2469-7680-7000-8000-000000000001';
  const supplierId = '019a2469-7680-7000-8000-000000000002';
  const lineId = '019a2469-7680-7000-8000-000000000003';
  const productId = '019a2469-7680-7000-8000-000000000004';
  beforeEach(() => {
    receive = vi.fn();
    getOrder = vi.fn();
    listOrders = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: { receive, get: getOrder, list: listOrders } },
      { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: SessionService, useValue: { has: (permission: string) => permission === 'PURCHASES.RECEIVE' || permission === 'PURCHASE_ORDERS.MANAGE', hasRole: (...codes: string[]) => codes.includes('SUPERVISOR'), user: () => ({ roles: ['SUPERVISOR'] }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Purchases());
    component.products.set([{ id: productId, retailPrice: 1000 } as Product]);
    component.selectedOrder.set({ id: orderId, version: 5, supplierId, status: 'RECIBIDO_PARCIAL', visibleNumber: 'PED-1', createdAt: '2026-10-24T12:00:00Z', sentAt: '2026-10-24T12:10:00Z', closedAt: null, lines: [
      { id: lineId, productId, productCode: 'P1', productName: 'Producto', requestedQuantity: 10, receivedQuantity: 6, pendingQuantity: 4, excessQuantity: 0, suggestedQuantity: 10, estimatedUnitCost: 100, observation: null },
    ] });
    component.startReceipt(); component.receipt.invoiceDate = '2026-10-24';
  });
  it('rechaza cantidad mayor que la pendiente sin enviar HTTP', async () => { component.receipt.lines[0].quantity = 5; expect(component.receiptValidation()).toContain('pendiente'); await component.receive(); expect(receive).not.toHaveBeenCalled(); });
  it('rechaza cantidades fraccionarias y costo inválido', () => { component.receipt.lines[0].quantity = 1.5; expect(component.receiptValidation()).toContain('entera'); component.receipt.lines[0].quantity = 2; component.receipt.lines[0].unitCost = 0; expect(component.receiptValidation()).toContain('costo'); });
  it('muestra el precio anterior y restringe el precio nuevo al Administrador', () => { expect(component.productRetailPrice(productId)).toBe(1000); expect(component.receipt.lines[0].newRetailPrice).toBeNull(); expect(component.canChangeRetailPrice).toBe(false); component.receipt.lines[0].quantity = 2; component.receipt.lines[0].newRetailPrice = 1000.5; expect(component.receiptValidation()).toContain('precio nuevo'); });
  it('un producto sin precio llega marcado para ponerle precio y no se recibe sin él (pruebas del 26/11/2026)', () => {
    component.products.set([{ id: productId, retailPrice: 0 } as Product]);
    component.startReceipt(); component.receipt.invoiceDate = '2026-10-24';
    expect(component.hasNoPrice(productId)).toBe(true);
    expect(component.newPrice[lineId]).toBe(true);
    component.receipt.lines[0].quantity = 2;
    expect(component.receiptValidation()).toContain('todavía no tiene precio de venta');
    component.receipt.lines[0].newRetailPrice = 1500;
    expect(component.receiptValidation()).toBe('');
    component.receipt.lines[0].unitCost = 0;
    expect(component.receiptValidation()).toContain('Escribe el costo de');
  });
  it('el precio nuevo es opcional: la recepción arranca en blanco y se puede enviar sin precio', () => {
    expect(component.receipt.lines[0].newRetailPrice).toBeNull();
    component.receipt.lines[0].quantity = 2;
    expect(component.receiptValidation()).toBe('');
  });
  it('la calculadora pone en «precio nuevo» el costo más la ganancia, redondeado de 50 en 50', () => {
    const line = component.receipt.lines[0]; line.unitCost = 1234;
    component.profit[line.orderLineId] = 25; component.applyProfit(line.orderLineId, line);
    expect(line.newRetailPrice).toBe(1550);
    component.profit[line.orderLineId] = null; line.newRetailPrice = null; component.applyProfit(line.orderLineId, line);
    expect(line.newRetailPrice).toBeNull(); expect(component.error()).toContain('% de ganancia');
  });
  it('avisa cuánto se gana, o que el precio queda por debajo del costo', () => {
    const line = component.receipt.lines[0]; line.unitCost = 800;
    expect(component.profitNote(line)?.text).toBe('Con el precio actual ganas 25 %');
    line.newRetailPrice = 1200; expect(component.profitNote(line)?.text).toBe('Ganas 50 % sobre el costo');
    line.newRetailPrice = 700; expect(component.profitNote(line)).toEqual({ text: 'Por debajo del costo', low: true });
    line.unitCost = 0; expect(component.profitNote(line)).toBeNull();
  });
  it('impide doble envío mientras la recepción está pendiente', async () => {
    component.receipt.lines[0].quantity = 2;
    expect(component.receiptValidation()).toBe('');
    component.tab = 'orders';
    let resolveFirst!: (value: unknown) => void;
    receive.mockReturnValue(new Promise(resolve => { resolveFirst = resolve; }));
    const first = component.receive();
    expect(component.saving()).toBe(true);
    expect(receive).toHaveBeenCalledTimes(1);
    const [submittedOrder, idempotencyKey, payload] = receive.mock.calls[0];
    expect(submittedOrder).toMatchObject({ id: orderId, status: 'RECIBIDO_PARCIAL', version: 5 });
    expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(payload).toMatchObject({ invoiceDate: '2026-10-24', vatMode: 'NO_DISCRIMINADO', lines: [{ orderLineId: lineId, productId, quantity: 2, unitCost: 100, vatPercentage: null, newRetailPrice: null }] });

    await component.receive();
    expect(receive).toHaveBeenCalledTimes(1);
    expect(component.saving()).toBe(true);

    const updatedOrder = { ...submittedOrder, version: 6, lines: [{ ...submittedOrder.lines[0], receivedQuantity: 8, pendingQuantity: 2 }] };
    getOrder.mockResolvedValue(updatedOrder);
    listOrders.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    resolveFirst({ purchase: { id: '019a2469-7680-7000-8000-000000000005' }, orderId, orderStatus: 'RECIBIDO_PARCIAL', orderVersion: 6 });
    await first;
    expect(receive).toHaveBeenCalledTimes(1);
    expect(component.saving()).toBe(false);
    expect(component.receiving()).toBe(false);
    expect(component.selectedOrder()?.version).toBe(6);
    expect(getOrder).toHaveBeenCalledOnce();
    expect(listOrders).toHaveBeenCalledOnce();
  });
});

describe('purchase dates', () => {
  it('usa la fecha de Colombia aunque UTC ya haya cambiado de día', () => {
    expect(currentColombiaDate(new Date('2026-11-16T02:30:00Z'))).toBe('2026-11-15');
    expect(currentColombiaDate(new Date('2026-11-16T05:01:00Z'))).toBe('2026-11-16');
  });
});

describe('Purchases: proveedor nuevo en la compra sin pedido (pruebas del 26/11/2026)', () => {
  const createSupplier = vi.fn();
  const openAuthorization = vi.fn(async (prompt: { run: (credentials: { userName: string; password: string }) => Promise<void> }) => { await prompt.run({ userName: 'jefe', password: '1234' }); return true; });
  let roles: string[];
  function build(): Purchases {
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: {} }, { provide: AuthorizationDialog, useValue: { open: openAuthorization } },
      { provide: SuppliersApi, useValue: { create: createSupplier } }, { provide: CatalogApi, useValue: {} },
      { provide: SessionService, useValue: { has: (p: string) => ['SUPPLIERS.MANAGE', 'PURCHASES.RECEIVE'].includes(p), hasRole: (...codes: string[]) => codes.some(c => roles.includes(c)), user: () => ({ roles }) } },
    ] });
    return TestBed.runInInjectionContext(() => new Purchases());
  }
  beforeEach(() => { vi.clearAllMocks(); roles = ['ADMINISTRADOR']; });

  it('el Administrador lo crea ahí mismo y queda elegido para la compra', async () => {
    const component = build();
    createSupplier.mockResolvedValue({ id: 's-new', name: 'Distribuidora Nueva', isActive: true });
    expect(component.canCreateSupplier).toBe(true);
    component.startNewSupplier();
    await component.createSupplier();
    expect(createSupplier).not.toHaveBeenCalled();
    expect(component.error()).toContain('nombre');
    component.newSupplier = { name: ' Distribuidora Nueva ', documentNumber: '900123', phone: '' };
    await component.createSupplier();
    expect(createSupplier).toHaveBeenCalledWith({ name: 'Distribuidora Nueva', documentType: 'NIT', documentNumber: '900123', phone: null, address: null, email: null, observations: null });
    expect(component.form.supplierId).toBe('s-new');
    expect(component.suppliers().map(s => s.id)).toContain('s-new');
    expect(component.addingSupplier()).toBe(false);
  });

  it('el Supervisor lo crea con la autorización del Administrador; el Cajero no ve la opción', async () => {
    roles = ['SUPERVISOR'];
    const component = build();
    createSupplier.mockResolvedValue({ id: 's-sup', name: 'Ferretería Nueva', isActive: true });
    expect(component.canCreateSupplier).toBe(true);
    component.startNewSupplier();
    component.newSupplier = { name: 'Ferretería Nueva', documentNumber: '', phone: '3001234567' };
    await component.createSupplier();
    expect(openAuthorization).toHaveBeenCalled();
    expect(createSupplier).toHaveBeenCalledWith(expect.objectContaining({ name: 'Ferretería Nueva', phone: '3001234567' }), { userName: 'jefe', password: '1234' });
    expect(component.form.supplierId).toBe('s-sup');
    TestBed.resetTestingModule();
    roles = ['CAJERO'];
    const cashier = build();
    expect(cashier.canCreateSupplier).toBe(false);
  });
});

describe('Purchases: producto nuevo en la compra sin pedido (pruebas del 26/11/2026)', () => {
  const createProduct = vi.fn();
  const catalog = {
    createProduct,
    categories: vi.fn(async () => ({ items: [{ id: 'c1', name: 'Papelería', isActive: true }, { id: 'c2', name: 'Vieja', isActive: false }] })),
    units: vi.fn(async () => [{ id: 'u-paquete', code: 'PAQUETE' }, { id: 'u-unidad', code: 'UNIDAD' }]),
  };
  let permissions: string[];
  function build(): Purchases {
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: {} }, { provide: AuthorizationDialog, useValue: {} },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: catalog },
      { provide: SessionService, useValue: { has: (p: string) => permissions.includes(p), hasRole: () => false, user: () => ({ roles: ['SUPERVISOR'] }) } },
    ] });
    return TestBed.runInInjectionContext(() => new Purchases());
  }
  beforeEach(() => { vi.clearAllMocks(); permissions = ['PURCHASES.RECEIVE', 'PRODUCTS.CREATE']; });

  it('lo crea en Catálogo con el proveedor de la compra y lo agrega como línea con su costo', async () => {
    const component = build();
    await component.startPurchaseProduct();
    expect(component.error()).toContain('proveedor');
    expect(component.newPurchaseProduct()).toBeNull();
    component.form.supplierId = 's1';
    await component.startPurchaseProduct();
    expect(component.orderCategories().map(c => c.id)).toEqual(['c1']);
    const draft = component.newPurchaseProduct()!;
    Object.assign(draft, { name: ' Cuaderno rayado ', categoryId: 'c1', quantity: 4, unitCost: 1500, retailPrice: null });
    await component.addPurchaseProduct();
    expect(createProduct).not.toHaveBeenCalled();
    expect(component.error()).toContain('precio de venta');
    draft.retailPrice = 3000;
    createProduct.mockResolvedValue({ id: 'p-new', name: 'Cuaderno rayado', internalCode: 'PAP-000009', retailPrice: 3000, isActive: true });
    await component.addPurchaseProduct();
    expect(createProduct).toHaveBeenCalledWith(expect.objectContaining({ name: 'Cuaderno rayado', categoryId: 'c1', measurementUnitId: 'u-unidad', supplierId: 's1',
      retailPrice: 3000, initialQuantity: 0, initialUnitCost: null }), true, draft.key);
    expect(component.form.lines).toEqual([{ productId: 'p-new', quantity: 4, unitCost: 1500, vatPercentage: null, newRetailPrice: null }]);
    expect(component.newPurchaseProduct()).toBeNull();
    expect(component.notice()).toContain('Cuaderno rayado');
  });

  it('sin permiso para crear productos no se ofrece', async () => {
    permissions = ['PURCHASES.RECEIVE'];
    const component = build();
    component.form.supplierId = 's1';
    expect(component.canCreatePurchaseProduct).toBe(false);
    await component.startPurchaseProduct();
    expect(component.newPurchaseProduct()).toBeNull();
  });
});

describe('Purchases: buscar el producto escribiendo o con el lector (pruebas del 26/11/2026)', () => {
  function build(): Purchases {
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: {} }, { provide: AuthorizationDialog, useValue: {} },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: SessionService, useValue: { has: () => true, hasRole: () => true, user: () => ({ roles: ['ADMINISTRADOR'] }) } },
    ] });
    const component = TestBed.runInInjectionContext(() => new Purchases());
    component.products.set([
      { id: 'p1', name: 'Cuaderno rayado', internalCode: 'PAP-000004', barcode: 'CP000016', supplierId: 's1', isActive: true, availableStock: 5, averageCost: 1500 },
      { id: 'p2', name: 'Cuaderno cuadriculado', internalCode: 'PAP-000007', barcode: 'CP0000161', supplierId: 's1', isActive: true, availableStock: 2, averageCost: null },
      { id: 'p3', name: 'Martillo', internalCode: 'FER-000003', barcode: '7701234', supplierId: 's2', isActive: true, availableStock: 1, averageCost: 9000 },
      { id: 'p4', name: 'Cuaderno viejo', internalCode: 'PAP-000001', barcode: null, supplierId: 's1', isActive: false, availableStock: 0, averageCost: null },
    ] as never);
    return component;
  }

  it('en la compra el lector pone primero el código exacto y repetir el producto suma uno', () => {
    const component = build();
    const options = component.purchaseProductOptions();
    expect(options.map(option => option.value)).toEqual(['p1', 'p2', 'p3']);
    expect(suggestMatches(options, 'CP000016')[0].value).toBe('p1');
    expect(suggestMatches(options, 'cuaderno').map(option => option.value)).toEqual(['p1', 'p2']);
    component.pickPurchaseProduct('p1');
    component.pickPurchaseProduct('p1');
    component.pickPurchaseProduct('p2');
    expect(component.form.lines).toEqual([
      { productId: 'p1', quantity: 2, unitCost: 1500, vatPercentage: null, newRetailPrice: null },
      { productId: 'p2', quantity: 1, unitCost: 0, vatPercentage: null, newRetailPrice: null },
    ]);
    expect(component.productCode('p2')).toBe('PAP-000007');
  });

  it('«Pedir» desde Catálogo abre el pedido con el proveedor y el producto incluido (maqueta 11)', async () => {
    const component = build();
    vi.spyOn(component, 'newOrder').mockResolvedValue();
    await component.orderProductFromCatalog('s1', 'p2');
    expect(component.orderSupplierId).toBe('s1');
    expect(component.orderLines.find(line => line.productId === 'p2')).toMatchObject({ selected: true, quantity: 1 });
    expect(component.orderSearchNote()).toContain('Cuaderno cuadriculado');
  });

  it('en el pedido la búsqueda filtra la lista y Enter incluye el producto o suma uno', () => {
    const component = build();
    component.setOrderSupplier('s1');
    component.orderProductSearch = 'cuaderno';
    expect(component.visibleOrderLines().map(line => line.productId)).toEqual(['p1', 'p2']);
    component.pickOrderProduct();
    expect(component.orderSearchNote()).toContain('varios');
    component.orderProductSearch = 'cp000016';
    component.pickOrderProduct();
    expect(component.orderLines.find(line => line.productId === 'p1')).toMatchObject({ selected: true, quantity: 1 });
    expect(component.orderProductSearch).toBe('');
    component.orderProductSearch = 'CP000016';
    component.pickOrderProduct();
    expect(component.orderLines.find(line => line.productId === 'p1')?.quantity).toBe(2);
    expect(component.orderSearchNote()).toContain('cantidad 2');
    component.orderProductSearch = '7701234';
    component.pickOrderProduct();
    expect(component.orderSearchNote()).toContain('Martillo no está asignado a este proveedor');
    component.orderProductSearch = 'tornillo';
    component.pickOrderProduct();
    expect(component.orderSearchNote()).toContain('No hay productos');
    expect(component.visibleOrderLines()).toEqual([]);
  });
});
