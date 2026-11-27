import { TestBed } from '@angular/core/testing';
import { TicketPrinter } from './ticket';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { NO_SALE_DRAFT, SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

const USER = '019a0000-0000-7000-8000-000000000003';
const product = (over: Record<string, unknown> = {}) => ({ id: '019a0000-0000-7000-8000-000000000001', name: 'Camisa', internalCode: 'P-000001', barcode: '770001', categoryName: 'Ropa', isActive: true, availableStock: 5, retailPrice: 10000, wholesalePrice: null, ...over }) as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false, currentShift: { operatingDate: '2026-11-06', responsibleUserId: USER } } as any;

describe('Sales: punto de venta de contado', () => {
  let component: Sales;
  let perms: string[];
  const products = vi.fn(); const confirm = vi.fn(); const state = vi.fn(); const requestReprint = vi.fn(); const receipt = vi.fn(); const print = vi.fn(); const printingStatus = vi.fn(); const printJobs = vi.fn(); const retryPrintJob = vi.fn(); const openDrawerForCheckout = vi.fn();
  beforeEach(() => {
    perms = ['SALES.CREATE', 'SALES.REPRINT', 'CUSTOMERS.BASIC_MANAGE']; localStorage.removeItem('controlplus.sales.autoPrint'); vi.useFakeTimers(); vi.clearAllMocks(); state.mockResolvedValue(cash); openDrawerForCheckout.mockResolvedValue({ status: 'ENVIADO' }); print.mockResolvedValue(undefined); printingStatus.mockResolvedValue({ hasActiveProfile: false, profileName: null, columns: null }); printJobs.mockResolvedValue({ items: [] });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: SalesApi, useValue: { confirm, requestReprint, receipt, printingStatus, printJobs, retryPrintJob } }, { provide: CashApi, useValue: { state, openDrawerForCheckout } }, { provide: CatalogApi, useValue: { products } },
      { provide: AuthorizationDialog, useValue: { open: vi.fn() } }, { provide: TicketPrinter, useValue: { print } },
      { provide: SessionService, useValue: { has: (p: string) => perms.includes(p), hasRole: (...c: string[]) => c.includes('CAJERO'), user: () => ({ id: USER, userName: 'cajero', displayName: 'Cajero Uno', roles: ['CAJERO'] }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Sales());
    component.cashState.set(cash);
  });
  afterEach(() => vi.useRealTimers());

  it('en turno compartido decide con el rol de quien pasó el carné, no con el de la sesión abierta', () => {
    // Sesión abierta de un Cajero y carné activo de una Supervisora: vende la Supervisora.
    component.cashState.set({ ...cash, requiresOperatorSession: true, ownOperatorSession: { id: 's1', state: 'ACTIVA' }, activeOperatorRole: 'SUPERVISOR' });
    expect(component.isCashier).toBe(false);
    expect(component.maxDiscount).toBe(20);
    component.cashState.set({ ...cash, requiresOperatorSession: true, ownOperatorSession: { id: 's2', state: 'ACTIVA' }, activeOperatorRole: 'CAJERO' });
    expect(component.isCashier).toBe(true);
    expect(component.maxDiscount).toBe(5);
  });

  describe('búsqueda con sugerencias', () => {
    it('sugiere mientras se escribe, sin esperar Enter, y no busca con un solo carácter', async () => {
      products.mockResolvedValue({ items: [product()] });
      component.onSearchInput('c'); await vi.advanceTimersByTimeAsync(400);
      expect(products).not.toHaveBeenCalled();
      component.onSearchInput('ca'); await vi.advanceTimersByTimeAsync(300);
      expect(products).toHaveBeenCalledWith({ search: 'ca', page: 1, pageSize: 8 });
      expect(component.results()).toHaveLength(1);
    });
    it('espera una pausa al teclear: solo consulta por el último texto', async () => {
      products.mockResolvedValue({ items: [] });
      component.onSearchInput('ca'); await vi.advanceTimersByTimeAsync(100); component.onSearchInput('cam'); await vi.advanceTimersByTimeAsync(300);
      expect(products).toHaveBeenCalledTimes(1);
      expect(products.mock.calls[0][0].search).toBe('cam');
    });
    it('descarta una respuesta vieja que llega tarde', async () => {
      let resolveOld!: (v: unknown) => void;
      products.mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve; })).mockResolvedValueOnce({ items: [product({ name: 'Nueva' })] });
      component.onSearchInput('ca'); await vi.advanceTimersByTimeAsync(300);
      component.onSearchInput('cam'); await vi.advanceTimersByTimeAsync(300);
      resolveOld({ items: [product({ name: 'Vieja' })] }); await vi.advanceTimersByTimeAsync(0);
      expect(component.results()[0].name).toBe('Nueva');
    });
    it('navega con flechas y agrega con Enter', async () => {
      component.results.set([product({ id: 'a', name: 'A' }), product({ id: 'b', name: 'B' })]);
      const key = (k: string) => component.onSearchKey({ key: k, preventDefault() { /* sin efecto */ } } as KeyboardEvent);
      key('ArrowDown'); key('ArrowDown'); key('ArrowUp'); key('ArrowDown');
      expect(component.activeIndex()).toBe(1);
      key('Enter');
      expect(component.cart().map(l => l.product.id)).toEqual(['b']);
      expect(component.results()).toEqual([]); expect(component.search).toBe('');
    });
    it('Enter con un código exacto agrega el producto y avisa', async () => {
      products.mockResolvedValue({ items: [product(), product({ id: 'x', internalCode: 'P-2', barcode: '770002' })] });
      component.search = '770001'; await component.find();
      expect(component.cart()).toHaveLength(1);
      expect(component.feedback()).toContain('Camisa agregado');
    });
    it('con varias coincidencias muestra la lista; sin resultados lo dice', async () => {
      products.mockResolvedValue({ items: [product({ id: 'a' }), product({ id: 'b' })] });
      component.search = 'cam'; await component.find();
      expect(component.cart()).toHaveLength(0); expect(component.results()).toHaveLength(2); expect(component.feedbackKind()).toBe('info');
      products.mockResolvedValue({ items: [] }); component.search = 'zzz'; await component.find();
      expect(component.feedback()).toContain('No encontramos'); expect(component.feedbackKind()).toBe('error');
    });
    it('Ver productos muestra el catálogo con el campo vacío y lo oculta al pulsar de nuevo', async () => {
      products.mockResolvedValue({ items: [product({ id: 'a' }), product({ id: 'b' })] });
      await component.toggleCatalog();
      expect(products).toHaveBeenCalledWith({ page: 1, pageSize: 30 }); expect(component.results()).toHaveLength(2);
      await component.toggleCatalog(); expect(component.results()).toEqual([]);
    });
    it('Ver productos con texto lista las coincidencias sin agregar la única', async () => {
      products.mockResolvedValue({ items: [product()] });
      component.search = 'cam'; await component.toggleCatalog();
      expect(component.results()).toHaveLength(1); expect(component.cart()).toHaveLength(0);
    });
    it('Agregar con el campo vacío avisa en lugar de no hacer nada', async () => {
      await component.find(); expect(component.feedback()).toContain('Escribe o escanea'); expect(component.feedbackKind()).toBe('error');
    });
    it('no agrega agotados y respeta las existencias', () => {
      component.add(product({ availableStock: 0 })); expect(component.cart()).toHaveLength(0); expect(component.error()).toContain('agotado');
      const two = product({ availableStock: 2 }); component.add(two); component.add(two); component.add(two);
      expect(component.cart()[0].quantity).toBe(2); expect(component.error()).toContain('Solo hay 2');
    });
    it('normaliza la cantidad escrita a un entero dentro de las existencias', () => {
      component.add(product()); const line = component.cart()[0];
      line.quantity = 9; component.normalizeQuantity(line); expect(line.quantity).toBe(5);
      line.quantity = 0; component.normalizeQuantity(line); expect(line.quantity).toBe(1);
    });
  });

  describe('cobro', () => {
    beforeEach(() => { component.add(product({ retailPrice: 50000 })); });
    it('prellena el efectivo exacto y no hay cambio', () => {
      component.startCheckout();
      expect(component.checkoutMode()).toBe(true);
      expect([component.cashApplied, component.cashReceived, component.receivedTotal, component.remaining, component.change]).toEqual([50000, 50000, 50000, 0, 0]);
      expect(component.canConfirm).toBe(true);
    });
    it('los billetes suman: el primero reemplaza y los siguientes se agregan', () => {
      component.startCheckout();
      component.addCashBill(50000); component.addCashBill(50000);
      expect([component.cashReceived, component.change]).toEqual([100000, 50000]);
      component.typeCashReceived(60000); component.addCashBill(20000);
      expect(component.cashReceived).toBe(20000);
      component.clearCashReceived();
      expect(component.cashReceived).toBe(0);
    });
    it('abre el cajón una vez por entrada al cobro y vuelve a abrir después de regresar al carrito', async () => {
      component.startCheckout(); component.startCheckout(); await vi.advanceTimersByTimeAsync(0);
      expect(openDrawerForCheckout).toHaveBeenCalledOnce();
      component.returnToSale(); component.add(product({ id: 'otro', retailPrice: 10000 }));
      component.startCheckout(); await vi.advanceTimersByTimeAsync(0);
      expect(openDrawerForCheckout).toHaveBeenCalledTimes(2);
      expect(openDrawerForCheckout.mock.calls[0][0]).not.toBe(openDrawerForCheckout.mock.calls[1][0]);
      expect(component.error()).toBe('');
      openDrawerForCheckout.mockResolvedValueOnce({ status: 'FALLIDO' });
      component.returnToSale(); component.startCheckout(); await vi.advanceTimersByTimeAsync(0);
      expect(component.error()).toContain('Cajón');
    });
    it('si el cliente entrega más, el cambio es la diferencia', () => {
      component.startCheckout(); component.onCashReceived(100000);
      expect([component.cashApplied, component.receivedTotal, component.change, component.remaining]).toEqual([50000, 100000, 50000, 0]);
    });
    it('pago mixto: el medio nuevo recibe automáticamente lo que falta', () => {
      component.startCheckout(); component.onCashReceived(20000);
      expect(component.remaining).toBe(30000);
      component.addPayment();
      expect(component.activePayments()).toEqual(['EFECTIVO', 'TRANSFERENCIA']);
      expect([component.cashApplied, component.transferApplied, component.receivedTotal, component.remaining]).toEqual([20000, 30000, 50000, 0]);
    });
    it('al editar un medio, el otro se reajusta', () => {
      component.startCheckout(); component.onCashReceived(20000); component.addPayment();
      component.onElectronicAmount('TRANSFERENCIA', 10000);
      expect([component.cashApplied, component.transferApplied, component.remaining]).toEqual([40000, 10000, 0]);
    });
    it('no activa el pago mixto si el medio ya cubre todo y lo explica', () => {
      component.startCheckout(); component.addPayment();
      expect(component.activePayments()).toEqual(['EFECTIVO']); expect(component.error()).toContain('ya cubre todo el total');
    });
    it('al quitar el segundo medio vuelve a cubrir el total', () => {
      component.startCheckout(); component.onCashReceived(20000); component.addPayment(); component.removePayment('TRANSFERENCIA');
      expect(component.activePayments()).toEqual(['EFECTIVO']); expect(component.cashApplied).toBe(20000);
      component.onCashReceived(50000); expect(component.remaining).toBe(0);
    });
    it('permite confirmar una transferencia sin referencia y la envía como opcional', async () => {
      confirm.mockResolvedValue({ id: 's-transfer', visibleNumber: 'VTA-001-000011', total: 50000, change: 0, state: 'CONFIRMADA', payments: [], creditId: null, lines: [] });
      component.startCheckout(); component.setPaymentMethod('EFECTIVO', 'TRANSFERENCIA');
      expect(component.transferApplied).toBe(50000);
      await component.confirm();
      expect(confirm.mock.calls[0][0]).toEqual(expect.objectContaining({ payments: [{ methodCode: 'TRANSFERENCIA', appliedAmount: 50000, receivedAmount: null, reference: null }] }));
      expect(component.error()).toBe('');
    });
    it('el efectivo insuficiente sugiere el pago mixto', async () => {
      component.startCheckout(); component.onCashReceived(10000);
      await component.confirm();
      expect(component.error()).toContain('Pago mixto'); expect(confirm).not.toHaveBeenCalled();
    });
  });

  describe('después de vender', () => {
    const result = { id: 's1', visibleNumber: 'VTA-001-000010', total: 50000, change: 50000, state: 'CONFIRMADA', payments: [{ methodCode: 'EFECTIVO', methodName: 'Efectivo', appliedAmount: 50000, receivedAmount: 100000, reference: null }], creditId: null, occurredAtUtc: '2026-11-06T15:00:00Z', lines: [] };
    beforeEach(() => { confirm.mockResolvedValue(result); component.add(product({ retailPrice: 50000 })); component.startCheckout(); component.onCashReceived(100000); });
    it('envía el pago de contado y muestra la venta confirmada con el cambio', async () => {
      await component.confirm();
      expect(confirm.mock.calls[0][0]).toEqual(expect.objectContaining({ saleType: 'CONTADO', payments: [{ methodCode: 'EFECTIVO', appliedAmount: 50000, receivedAmount: 100000, reference: null }] }));
      expect(component.sale()?.visibleNumber).toBe('VTA-001-000010');
      expect(component.receipt()).toEqual({ customer: 'Consumidor final', operator: 'Cajero Uno', items: 1, units: 1 });
      expect(component.cart()).toEqual([]); expect(component.checkoutMode()).toBe(false);
    });
    it('conserva la venta confirmada cuando la orden del cajón falla', async () => {
      confirm.mockResolvedValue({ ...result, drawerStatus: 'FALLIDO' });
      await component.confirm();
      expect(component.sale()?.id).toBe('s1');
      expect(component.drawerStatus()).toBe('FALLIDO');
      expect(component.error()).toBe('');
      component.newSale();
      expect(component.drawerStatus()).toBeNull();
    });
    it('sin borrador guardado envía NO_SALE_DRAFT, para no cerrar ninguna venta en espera', async () => {
      await component.confirm();
      expect(confirm.mock.calls[0][0]).toEqual(expect.objectContaining({ draftId: NO_SALE_DRAFT }));
    });
    it('Enter en la pantalla de venta confirmada inicia una venta nueva', async () => {
      await component.confirm();
      component.handleShortcut({ key: 'Enter', target: null, preventDefault() { /* sin efecto */ } } as unknown as KeyboardEvent);
      expect(component.sale()).toBeNull(); expect(component.receipt()).toBeNull(); expect(component.saleType()).toBe('CONTADO');
    });
    it('el modal de detalle protege la venta confirmada y se cierra con Escape', async () => {
      await component.confirm(); component.showDetail.set(true);
      component.handleShortcut({ key: 'Enter', target: null, preventDefault() { /* sin efecto */ } } as unknown as KeyboardEvent);
      expect(component.sale()?.id).toBe('s1'); expect(component.showDetail()).toBe(true);
      component.handleShortcut({ key: 'Escape', target: null, preventDefault() { /* sin efecto */ } } as unknown as KeyboardEvent);
      expect(component.showDetail()).toBe(false); expect(component.sale()?.id).toBe('s1');
    });
    it('Nueva venta limpia al cliente y el descuento de la venta anterior', async () => {
      component.customerId = 'c1'; component.discountReason = 'x';
      await component.confirm(); component.newSale();
      expect([component.customerId, component.discountReason, component.sale()]).toEqual(['', '', null]);
    });
    it('la primera impresión no registra reimpresión; las siguientes sí', async () => {
      receipt.mockResolvedValue({ saleId: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '2026-11-06T15:00:00Z', lines: [], payments: [], total: 50000, change: 0, grossSubtotal: 50000, discountTotal: 0 });
      requestReprint.mockResolvedValue({ status: 'PENDIENTE' });
      // La primera impresión es la automática al confirmar; la del botón después ya es una reimpresión.
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(requestReprint).not.toHaveBeenCalled(); expect(print).toHaveBeenCalledTimes(1);
      expect(component.printed()).toBe(true); expect(component.reprintMessage()).toContain('enviado');
      await component.printSaleTicket();
      expect(requestReprint).toHaveBeenCalledWith('s1', expect.any(String)); expect(print).toHaveBeenCalledTimes(2);
      expect(component.reprintMessage()).toContain('registrada');
    });
    it('imprime solo al confirmar la venta, una vez, sin registrar reimpresión', async () => {
      receipt.mockResolvedValue({ saleId: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '2026-11-06T15:00:00Z', lines: [], payments: [], total: 50000, change: 0, grossSubtotal: 50000, discountTotal: 0 });
      await component.confirm();
      expect(print).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(10);
      expect(print).toHaveBeenCalledTimes(1); expect(requestReprint).not.toHaveBeenCalled();
      expect(component.printed()).toBe(true);
    });
    it('con perfil activo usa solo el backend y no abre la impresión del navegador', async () => {
      printingStatus.mockResolvedValue({ hasActiveProfile: true, profileName: 'Caja', columns: 32 });
      printJobs.mockResolvedValue({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'PENDIENTE' }] });
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(print).not.toHaveBeenCalled();
      expect(requestReprint).not.toHaveBeenCalled();
      expect(component.printed()).toBe(true);
      expect(component.reprintMessage()).toContain('Pendiente');
    });
    it('sigue el ticket pendiente hasta que la cola lo acepta', async () => {
      printingStatus.mockResolvedValue({ hasActiveProfile: true, profileName: 'Caja', columns: 32 });
      printJobs.mockResolvedValueOnce({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'PENDIENTE' }] })
        .mockResolvedValueOnce({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'PENDIENTE' }] })
        .mockResolvedValue({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'IMPRESO' }] });
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(component.lastJobStatus()).toBe('PENDIENTE');
      await vi.advanceTimersByTimeAsync(1500);
      expect(component.lastJobStatus()).toBe('PENDIENTE');
      await vi.advanceTimersByTimeAsync(1500);
      expect(component.lastJobStatus()).toBe('IMPRESO');
      expect(component.reprintMessage()).toContain('La impresora lo recibió');
      const calls = printJobs.mock.calls.length;
      await vi.advanceTimersByTimeAsync(6000);
      expect(printJobs.mock.calls.length).toBe(calls);
    });
    it('consulta el ticket aunque actualizar la caja tarde, y no se queda en «sin imprimir» (27/11/2026)', async () => {
      printingStatus.mockResolvedValue({ hasActiveProfile: true, profileName: 'Caja', columns: 32 });
      printJobs.mockResolvedValue({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'IMPRESO' }] });
      state.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(cash), 300)));
      const confirming = component.confirm();
      await vi.advanceTimersByTimeAsync(400); await confirming; await vi.advanceTimersByTimeAsync(10);
      expect(printingStatus).toHaveBeenCalled();
      expect(component.printed()).toBe(true);
      expect(component.lastJobStatus()).toBe('IMPRESO');
    });
    it('si no puede consultar el perfil no abre el navegador ni duplica el ticket', async () => {
      printingStatus.mockRejectedValue(new Error('Sin conexión'));
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(print).not.toHaveBeenCalled();
      expect(component.sale()).not.toBeNull();
      expect(component.reprintMessage()).not.toBe('');
    });
    it('reintenta un trabajo ERROR existente sin pedir otra copia', async () => {
      printingStatus.mockResolvedValue({ hasActiveProfile: true, profileName: 'Caja', columns: 32 });
      printJobs.mockResolvedValueOnce({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'ERROR' }] })
        .mockResolvedValueOnce({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'ERROR' }] })
        .mockResolvedValue({ items: [{ id: 'j1', copyType: 'ORIGINAL', status: 'PENDIENTE' }] });
      retryPrintJob.mockResolvedValue({ id: 'j1', status: 'PENDIENTE' });
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      await component.printSaleTicket();
      expect(retryPrintJob).toHaveBeenCalledWith('s1', 'j1');
      expect(requestReprint).not.toHaveBeenCalled();
      expect(print).not.toHaveBeenCalled();
    });
    it('con la impresión automática apagada no imprime y la preferencia se recuerda', async () => {
      component.setAutoPrint(false);
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(print).not.toHaveBeenCalled(); expect(component.printed()).toBe(false);
      expect(localStorage.getItem('controlplus.sales.autoPrint')).toBe('0');
      component.setAutoPrint(true); expect(localStorage.getItem('controlplus.sales.autoPrint')).toBe('1');
    });
    it('si la impresión automática falla, la venta sigue confirmada y se puede reintentar', async () => {
      receipt.mockResolvedValue({ saleId: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '2026-11-06T15:00:00Z', lines: [], payments: [], total: 50000, change: 0, grossSubtotal: 50000, discountTotal: 0 });
      print.mockRejectedValueOnce(new Error('sin impresora'));
      await component.confirm(); await vi.advanceTimersByTimeAsync(10);
      expect(component.sale()?.visibleNumber).toBe('VTA-001-000010'); expect(component.printed()).toBe(false); expect(component.reprintMessage()).not.toBe('');
      await component.printSaleTicket();
      expect(component.printed()).toBe(true); expect(requestReprint).not.toHaveBeenCalled();
    });
    it('si la impresión falla muestra el error y no la da por enviada', async () => {
      receipt.mockResolvedValue({}); print.mockRejectedValueOnce(new Error('sin impresora'));
      await component.confirm(); await component.printSaleTicket();
      expect(component.printed()).toBe(false);
    });
  });

  describe('selector de cliente', () => {
    const ana = { id: '019a0000-0000-7000-8000-000000000002', fullName: 'Ana', isActive: true, phone: '300', documentType: null, documentNumber: null } as any;
    const press = (key: string) => { const e = { key, target: null, preventDefault: vi.fn() } as unknown as KeyboardEvent; component.handleShortcut(e); return e; };
    it('F3 abre la ventana de clientes si hay permiso y no si falta', () => {
      press('F3'); expect(component.showCustomer()).toBe(true);
      component.showCustomer.set(false); perms = ['SALES.CREATE']; press('F3'); expect(component.showCustomer()).toBe(false);
    });
    it('con la ventana abierta los atajos de Ventas no actúan', () => {
      component.add(product()); component.showCustomer.set(true);
      press('F9'); expect(component.checkoutMode()).toBe(false);
    });
    it('al elegir un cliente queda en la venta con su nombre; consumidor final lo quita', () => {
      component.openCustomerPicker(); component.onCustomerChosen(ana);
      expect(component.showCustomer()).toBe(false); expect(component.customerId).toBe(ana.id); expect(component.selectedCustomerName).toBe('Ana');
      component.onCustomerChosen(null); expect(component.customerId).toBe(''); expect(component.selectedCustomerName).toBe('Consumidor final');
    });
    it('no se abre mientras hay una confirmación pendiente', async () => {
      confirm.mockRejectedValueOnce(new Error('sin respuesta'));
      component.add(product({ retailPrice: 50000 })); component.startCheckout(); await component.confirm();
      component.openCustomerPicker(); expect(component.showCustomer()).toBe(false);
    });
    it('un crédito sin cliente abre la ventana y al elegirlo sigue al cobro', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST', 'CUSTOMERS.BASIC_MANAGE']; component.add(product());
      component.startCreditSale();
      expect(component.saleType()).toBe('CREDITO'); expect(component.showCustomer()).toBe(true); expect(component.checkoutMode()).toBe(false);
      component.onCustomerChosen(ana);
      expect(component.checkoutMode()).toBe(true);
    });
    it('«Continuar crédito» o F9 sin cliente abren la ventana para elegirlo o crearlo, en vez de fallar', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST', 'CUSTOMERS.BASIC_MANAGE']; component.add(product());
      component.saleType.set('CREDITO');
      press('F9');
      expect(component.showCustomer()).toBe(true); expect(component.checkoutMode()).toBe(false);
      component.onCustomerChosen({ ...ana, id: 'recien-creado' });
      expect(component.checkoutMode()).toBe(true); expect(component.customerId).toBe('recien-creado');
    });
    it('con el cliente ya elegido, el crédito pasa directo al cobro', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST', 'CUSTOMERS.BASIC_MANAGE']; component.add(product());
      component.onCustomerChosen(ana); component.startCreditSale();
      expect(component.showCustomer()).toBe(false); expect(component.checkoutMode()).toBe(true);
    });
    it('sin permiso de clientes explica por qué no puede continuar el crédito', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST']; component.add(product());
      component.startCreditSale();
      expect(component.showCustomer()).toBe(false); expect(component.checkoutMode()).toBe(false); expect(component.error()).toContain('permiso');
    });
    it('desde el cobro del crédito se puede cambiar el cliente', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST', 'CUSTOMERS.BASIC_MANAGE']; component.add(product());
      component.onCustomerChosen(ana); component.startCreditSale();
      component.openCustomerPicker(); expect(component.showCustomer()).toBe(true);
      component.onCustomerChosen({ ...ana, id: 'otro', fullName: 'Otro' });
      expect(component.customerId).toBe('otro'); expect(component.checkoutMode()).toBe(true);
    });
    it('cerrar la ventana sin elegir no continúa al cobro', () => {
      perms = ['SALES.CREATE', 'CREDIT.REQUEST', 'CUSTOMERS.BASIC_MANAGE']; component.add(product());
      component.startCreditSale(); component.closeCustomerPicker();
      expect(component.showCustomer()).toBe(false); expect(component.checkoutMode()).toBe(false);
      component.onCustomerChosen(ana); expect(component.checkoutMode()).toBe(false);
    });
  });

  describe('atajos y bloqueos', () => {
    const press = (key: string) => { const e = { key, target: null, preventDefault: vi.fn() } as unknown as KeyboardEvent; component.handleShortcut(e); return e; };
    it('F9 pasa al cobro, Esc vuelve y F9 en el cobro confirma', async () => {
      component.add(product()); confirm.mockResolvedValue({ id: 's', visibleNumber: 'V', total: 10000, change: 0, state: 'CONFIRMADA', payments: [], creditId: null });
      expect(press('F9').preventDefault).toHaveBeenCalled(); expect(component.checkoutMode()).toBe(true);
      press('Escape'); expect(component.checkoutMode()).toBe(false);
      press('F9'); press('F9'); await vi.advanceTimersByTimeAsync(0);
      expect(confirm).toHaveBeenCalledTimes(1);
    });
    it('F4 selecciona la primera línea y Supr quita la seleccionada', () => {
      component.add(product({ id: 'a' })); component.add(product({ id: 'b' })); component.selectedProductId.set('a');
      press('Delete'); expect(component.cart().map(l => l.product.id)).toEqual(['b']);
      press('F4'); expect(component.selectedProductId()).toBe('b');
    });
    it('Cancelar venta vacía el carrito', () => {
      component.add(product()); component.showCancel.set(true); component.cancelSale();
      expect(component.cart()).toEqual([]); expect(component.showCancel()).toBe(false);
    });
    it('explica por qué no se puede vender', () => {
      component.cashState.set({ ...cash, hasOpenShift: false });
      expect(component.blockReason).toContain('Abre el turno');
      component.cashState.set({ ...cash, requiresPreviousShiftClosure: true }); expect(component.blockReason).toContain('anterior');
      component.cashState.set({ ...cash, requiresOperatorSession: true, ownOperatorSession: null }); expect(component.blockReason).toContain('sesión de operador');
      component.cashState.set({ ...cash, currentShift: { ...cash.currentShift, responsibleUserId: 'otro' } }); expect(component.blockReason).toContain('otro usuario');
      component.cashState.set({ ...cash, currentShift: { ...cash.currentShift, responsibleUserId: 'otro' }, openShiftResponsibleName: 'Marta Ruiz' }); expect(component.blockReason).toContain('abierta por Marta Ruiz');
      component.cashState.set(cash); expect(component.blockReason).toBe('');
    });
  });
});
