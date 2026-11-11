import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { TicketPrinter } from './ticket';

const USER = '019a0000-0000-7000-8000-000000000003';
const product = (over: Record<string, unknown> = {}) => ({ id: 'p1', name: 'Camisa', internalCode: 'P-1', isActive: true, availableStock: 50, retailPrice: 10000, wholesalePrice: 8000, ...over }) as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false, currentShift: { operatingDate: '2026-11-06', responsibleUserId: USER } } as any;
type D = { type: 'PORCENTAJE' | 'VALOR_FIJO'; value: number };

describe('Sales: descuentos y precio mayorista con autorización', () => {
  let component: Sales;
  let perms: string[]; let roles: string[];
  const confirm = vi.fn(); const verifyConcession = vi.fn();
  let prompts: AuthorizationPrompt[]; let dialogError: unknown; let acceptDialog: boolean;
  /** Simulates the window: the cashier types credentials and `run` validates them; cancelling returns false. */
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => {
    prompts.push(prompt); dialogError = undefined;
    if (!acceptDialog) return false;
    try { await prompt.run({ userName: 'jefe', password: 'clave' }); return true; } catch (e) { dialogError = e; return false; }
  }) };

  beforeEach(() => {
    vi.clearAllMocks(); prompts = []; dialogError = undefined; acceptDialog = true; perms = ['SALES.CREATE', 'SALES.DISCOUNT_APPLY']; roles = ['ADMINISTRADOR'];
    confirm.mockResolvedValue({ id: 's', visibleNumber: 'V', total: 0, change: 0, state: 'CONFIRMADA', payments: [], creditId: null });
    verifyConcession.mockResolvedValue({ authorizedBy: 'Jefe Supervisor' });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: SalesApi, useValue: { confirm, verifyConcession, printingStatus: vi.fn().mockResolvedValue({ hasActiveProfile: false }), printJobs: vi.fn().mockResolvedValue({ items: [] }), receipt: vi.fn(), requestReprint: vi.fn() } },
      { provide: CashApi, useValue: { state: vi.fn().mockResolvedValue(cash) } }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: dialog }, { provide: TicketPrinter, useValue: { print: vi.fn() } },
      { provide: SessionService, useValue: { has: (p: string) => perms.includes(p), hasRole: (...c: string[]) => c.some(x => roles.includes(x)), user: () => ({ id: USER, userName: 'cajero', displayName: 'Cajero', roles }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Sales());
    component.cashState.set(cash); component.add(product());
    component.cart.update(lines => lines.map(line => ({ ...line, quantity: 2 })));   // bruto 20.000
  });

  const apply = async (scope: 'product' | 'sale', discount: D, reason = 'Promo') => { component.openDiscount(scope); await component.applyDiscount({ discount, reason }); };
  const setQuantity = (quantity: number) => { const line = component.cart()[0]; line.quantity = quantity; component.normalizeQuantity(line); return line; };
  const select = (value: string) => ({ value } as HTMLSelectElement);

  describe('Administrador y Supervisor', () => {
    it.each(['ADMINISTRADOR', 'SUPERVISOR'])('%s descuenta sin ventana de autorización y sin enviar credenciales', async role => {
      roles = [role];
      await apply('sale', { type: 'PORCENTAJE', value: 10 }, '');
      expect(prompts).toEqual([]); expect(verifyConcession).not.toHaveBeenCalled(); expect(component.needsConcessionAuthorization).toBe(false);
      component.startCheckout(); await component.confirm();
      expect(confirm.mock.calls[0][0].authorization).toBeNull(); expect(confirm.mock.calls[0][0].discountReason).toBeNull();
    });

    it.each(['ADMINISTRADOR', 'SUPERVISOR'])('%s elige precio mayorista desde 12 unidades sin autorización', async role => {
      roles = [role]; const line = setQuantity(12);
      expect(await component.setPriceType(line, 'MAYORISTA')).toBe(true);
      expect(prompts).toEqual([]); expect(line.priceType).toBe('MAYORISTA'); expect(component.price(line)).toBe(8000);
    });
  });

  describe('Cajero: descuento', () => {
    beforeEach(() => { roles = ['CAJERO']; });

    it('al pulsar Aplicar aparece la ventana de autorización y el descuento se aplica al autorizar', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 });
      expect(prompts).toHaveLength(1); expect(prompts[0].title).toBe('Autorizar descuento'); expect(prompts[0].requireDifferentUser).toBe(true); expect(prompts[0].selfAuthorizedRoles).toBeUndefined();
      expect(verifyConcession).toHaveBeenCalledWith('DISCOUNT', 'jefe', 'clave');
      expect(component.generalDiscount).toEqual({ type: 'PORCENTAJE', value: 5 }); expect(component.concessionAuthorizedBy()).toBe('Jefe Supervisor');
    });

    it('si el autorizador es rechazado el descuento NO se aplica y la ventana explica por qué', async () => {
      verifyConcession.mockRejectedValue(new HttpErrorResponse({ status: 403 }));
      await apply('product', { type: 'PORCENTAJE', value: 5 });
      expect((dialogError as Error).message).toContain('No se autorizó');
      expect(component.cart()[0].discount).toBeNull(); expect(component.discounts).toBe(0); expect(component.concessionAuthorizedBy()).toBe('');
    });

    it('si se cierra la ventana sin autorizar tampoco se aplica', async () => {
      acceptDialog = false;
      await apply('sale', { type: 'PORCENTAJE', value: 5 });
      expect(component.generalDiscount).toBeNull(); expect(verifyConcession).not.toHaveBeenCalled();
    });

    it('con el motivo opcional vacío igual pide la autorización y la venta viaja con las credenciales verificadas', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 }, '');
      component.startCheckout(); await component.confirm();
      expect(prompts).toHaveLength(1);   // solo la de aplicar; al cobrar no vuelve a pedirla
      expect(confirm.mock.calls[0][0].authorization).toEqual({ userName: 'jefe', password: 'clave' });
      expect(confirm.mock.calls[0][0].discountReason).toBeNull();
    });

    it('al terminar la venta olvida las credenciales', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 });
      component.startCheckout(); await component.confirm();
      expect(component.concessionAuthorizedBy()).toBe(''); expect(component.needsConcessionAuthorization).toBe(false);
    });

    it('quitar el descuento olvida la autorización', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 }); component.removeDiscount();
      expect(component.concessionAuthorizedBy()).toBe(''); component.startCheckout(); await component.confirm();
      expect(confirm.mock.calls[0][0].authorization).toBeNull();
    });

    it('cancelar la venta olvida el descuento y las credenciales', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 }); component.cancelSale();
      expect([component.generalDiscount, component.discountReason, component.concessionAuthorizedBy()]).toEqual([null, '', '']);
    });

    it('si las credenciales se perdieron, las pide de nuevo al cobrar', async () => {
      component.generalDiscount = { type: 'PORCENTAJE', value: 5 };   // descuento sin pasar por la ventana
      component.startCheckout(); await component.confirm();
      expect(prompts).toHaveLength(1); expect(prompts[0].title).toContain('descuento o precio mayorista');
      expect(confirm.mock.calls[0][0].authorization).toEqual({ userName: 'jefe', password: 'clave' });
    });

    it('si el servidor rechaza al autorizador al cobrar, olvida las credenciales y pide autorizar de nuevo', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 5 });
      confirm.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
      component.startCheckout(); await component.confirm();
      expect(component.concessionAuthorizedBy()).toBe(''); expect(component.error()).toContain('Aplica la autorización de nuevo'); expect(component.locked).toBe(false);
    });

    it('el límite del 5 % se vuelve a validar al confirmar', async () => {
      component.generalDiscount = { type: 'VALOR_FIJO', value: 5000 };   // 25 % de 20.000
      component.startCheckout(); await component.confirm();
      expect(confirm).not.toHaveBeenCalled(); expect(component.error()).toContain('hasta 5 %');
    });

    it('sin descuento ni mayorista no se pide nada', async () => {
      component.startCheckout(); await component.confirm();
      expect(prompts).toEqual([]); expect(confirm).toHaveBeenCalledOnce();
    });
  });

  describe('precio mayorista', () => {
    it('exige 12 o más unidades: con menos no cambia, avisa y no pide autorización', async () => {
      roles = ['CAJERO']; const line = setQuantity(11);
      expect(await component.setPriceType(line, 'MAYORISTA')).toBe(false);
      expect(line.priceType).toBe('MINORISTA'); expect(component.error()).toContain('12'); expect(prompts).toEqual([]);
    });

    it('el Cajero con 12 unidades abre la ventana de autorización y queda en mayorista al autorizar', async () => {
      roles = ['CAJERO']; const line = setQuantity(12);
      expect(await component.setPriceType(line, 'MAYORISTA')).toBe(true);
      expect(prompts[0].title).toBe('Autorizar precio mayorista'); expect(verifyConcession).toHaveBeenCalledWith('WHOLESALE', 'jefe', 'clave');
      expect([line.priceType, component.price(line), component.concessionAuthorizedBy()]).toEqual(['MAYORISTA', 8000, 'Jefe Supervisor']);
    });

    it('si no se autoriza, el producto sigue en minorista y el desplegable vuelve a su valor', async () => {
      roles = ['CAJERO']; acceptDialog = false; const line = setQuantity(12); const control = select('MAYORISTA');
      await component.onPriceTypeChange(line, control);
      expect(line.priceType).toBe('MINORISTA'); expect(control.value).toBe('MINORISTA');
    });

    it('un producto sin precio mayorista no cambia', async () => {
      const plain = product({ id: 'n', name: 'Sin mayorista', wholesalePrice: null }); component.add(plain);
      const line = component.cart()[1]; line.quantity = 12;
      expect(await component.setPriceType(line, 'MAYORISTA')).toBe(false); expect(component.error()).toContain('no tiene precio mayorista');
    });

    it('al bajar la cantidad de una línea mayorista por debajo de 12 vuelve a minorista y olvida la autorización', async () => {
      roles = ['CAJERO']; const line = setQuantity(12); await component.setPriceType(line, 'MAYORISTA');
      setQuantity(5);
      expect(line.priceType).toBe('MINORISTA'); expect(component.feedback()).toContain('volvió a precio minorista'); expect(component.concessionAuthorizedBy()).toBe('');
    });

    it('la venta viaja con el precio mayorista y las credenciales verificadas', async () => {
      roles = ['CAJERO']; const line = setQuantity(12); await component.setPriceType(line, 'MAYORISTA');
      component.startCheckout(); await component.confirm();
      expect(confirm.mock.calls[0][0].lines[0]).toMatchObject({ quantity: 12, priceType: 'MAYORISTA' });
      expect(confirm.mock.calls[0][0].authorization).toEqual({ userName: 'jefe', password: 'clave' });
    });

    it('no se puede confirmar una línea mayorista con menos de 12 unidades', async () => {
      const line = component.cart()[0]; line.priceType = 'MAYORISTA'; line.quantity = 3;
      component.startCheckout(); await component.confirm();
      expect(confirm).not.toHaveBeenCalled(); expect(component.error()).toContain('12');
    });

    it('elegir mayorista quita el descuento de la línea y el general, porque no se acumulan', async () => {
      const line = setQuantity(12); line.discount = { type: 'PORCENTAJE', value: 5 }; component.discountReason = 'x';
      await component.setPriceType(line, 'MAYORISTA');
      expect(line.discount).toBeNull(); expect(component.discountReason).toBe(''); expect(component.feedback()).toContain('no admite descuento');
      await component.setPriceType(line, 'MINORISTA'); component.generalDiscount = { type: 'VALOR_FIJO', value: 100 }; await component.setPriceType(line, 'MAYORISTA');
      expect(component.generalDiscount).toBeNull();
    });
  });

  describe('descuento sin autorización cuando hay precio mayorista y otras reglas', () => {
    it('el descuento de venta total no se abre si hay líneas mayoristas', () => {
      component.cart.update(lines => lines.map(line => ({ ...line, quantity: 12, priceType: 'MAYORISTA' as const })));
      component.openDiscount('product'); expect(component.discountScope()).toBeNull(); expect(component.error()).toContain('minorista');
      component.error.set(''); component.openDiscount('sale'); expect(component.discountScope()).toBeNull(); expect(component.error()).toContain('mayorista');
    });

    it('sin SALES.DISCOUNT_APPLY no se abre ninguna ventana', () => {
      perms = ['SALES.CREATE']; component.openDiscount('product'); component.openDiscount('sale');
      expect(component.discountScope()).toBeNull();
    });

    it('con una confirmación pendiente no se puede cambiar el descuento ni el tipo de precio', async () => {
      confirm.mockRejectedValueOnce(new Error('sin respuesta'));
      component.startCheckout(); await component.confirm();
      expect(component.locked).toBe(true); component.openDiscount('sale'); expect(component.discountScope()).toBeNull();
      const line = setQuantity(12); expect(await component.setPriceType(line, 'MAYORISTA')).toBe(false);
    });
  });

  describe('modalidades del descuento', () => {
    it('Desc. producto abre la ventana sobre la línea seleccionada', () => {
      component.openDiscount('product');
      expect(component.discountScope()).toBe('product'); expect(component.discountTarget?.product.id).toBe('p1');
    });

    it('un descuento en pesos al producto baja el total y viaja como VALOR_FIJO en la línea', async () => {
      await apply('product', { type: 'VALOR_FIJO', value: 3000 });
      expect(component.discounts).toBe(3000); expect(component.estimate).toBe(17000); expect(component.discountScope()).toBeNull();
      component.startCheckout(); await component.confirm();
      const body = confirm.mock.calls[0][0];
      expect(body.lines[0].discount).toEqual({ type: 'VALOR_FIJO', value: 3000 }); expect(body.generalDiscount).toBeNull(); expect(body.discountReason).toBe('Promo');
    });

    it('un porcentaje al producto se calcula sobre el valor de la línea', async () => {
      await apply('product', { type: 'PORCENTAJE', value: 10 });
      expect(component.lineDiscount(component.cart()[0])).toBe(2000); expect(component.estimate).toBe(18000);
    });

    it('la venta total admite porcentaje o valor en pesos y viaja como descuento general', async () => {
      await apply('sale', { type: 'VALOR_FIJO', value: 5000 });
      expect(component.discounts).toBe(5000); expect(component.estimate).toBe(15000);
      component.startCheckout(); await component.confirm();
      const body = confirm.mock.calls[0][0];
      expect(body.generalDiscount).toEqual({ type: 'VALOR_FIJO', value: 5000 }); expect(body.lines[0].discount).toBeNull();
    });

    it('el descuento de la venta total reemplaza los de producto, y viceversa', async () => {
      await apply('product', { type: 'PORCENTAJE', value: 10 });
      component.openDiscount('sale'); expect(component.discountReplaces).toContain('por producto');
      await component.applyDiscount({ discount: { type: 'PORCENTAJE', value: 5 }, reason: 'x' });
      expect(component.cart()[0].discount).toBeNull(); expect(component.generalDiscount?.value).toBe(5);
      component.openDiscount('product'); expect(component.discountReplaces).toContain('venta total');
      await component.applyDiscount({ discount: { type: 'VALOR_FIJO', value: 1000 }, reason: 'y' });
      expect(component.generalDiscount).toBeNull(); expect(component.cart()[0].discount).toEqual({ type: 'VALOR_FIJO', value: 1000 });
    });

    it('Quitar descuento limpia el descuento y el motivo', async () => {
      await apply('sale', { type: 'PORCENTAJE', value: 10 }); component.removeDiscount();
      expect(component.generalDiscount).toBeNull(); expect(component.discounts).toBe(0); expect(component.discountReason).toBe('');
    });

    it('un producto con descuento no se mezcla con otra unidad del mismo producto sin descuento', async () => {
      await apply('product', { type: 'PORCENTAJE', value: 10 }); component.add(product());
      expect(component.cart()).toHaveLength(2);
    });
  });
});
