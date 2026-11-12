import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { CustomersApi, Customer } from '../customers/customers-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { dateInDays, LayawayCreate, minimumInitialPayment } from './layaway-create';
import { LayawaysApi } from './layaways-api';

const product = (extra: Partial<Product> = {}): Product => ({ id: 'p1', internalCode: 'PAP-000001', name: 'Cuaderno', retailPrice: 10000, wholesalePrice: 8000,
  availableStock: 5, isActive: true, ...extra } as Product);
const customer = { id: 'c1', fullName: 'Ana', documentNumber: '123', phone: null, address: 'Calle 1', isActive: true } as Customer;

describe('LayawayCreate: nuevo apartado', () => {
  const api = { create: vi.fn(), creationRules: vi.fn() };
  const customers = { list: vi.fn() };
  const catalog = { products: vi.fn() };
  const state = { roles: ['CAJERO'] };
  const session = { has: (code: string) => ['CUSTOMERS.BASIC_MANAGE'].includes(code), hasRole: (...codes: string[]) => codes.some(c => state.roles.includes(c)), user: () => ({ userName: 'cajera' }) };
  let prompt: AuthorizationPrompt | null;
  let dialogError: unknown;
  let credentials = { userName: 'supervisor', password: 'secreto' };
  const dialog = { open: vi.fn(async (p: AuthorizationPrompt) => { prompt = p; dialogError = undefined; try { await p.run(credentials); return true; } catch (e) { dialogError = e; return false; } }) };
  let component: LayawayCreate;
  let created: unknown[];

  const fill = () => {
    component.selectCustomer(customer);
    component.add(product());
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 8000, reference: '' }];
  };

  beforeEach(() => {
    state.roles = ['CAJERO'];
    vi.clearAllMocks(); prompt = null; dialogError = undefined; credentials = { userName: 'supervisor', password: 'secreto' };
    api.create.mockResolvedValue({ id: 'a1', visibleNumber: 'APT-1', total: 10000, balance: 0 });
    api.creationRules.mockResolvedValue({ requireMinimumInitial: true, minimumPercentage: 20, defaultDays: 15 });
    customers.list.mockResolvedValue({ items: [customer], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    catalog.products.mockResolvedValue({ items: [product()], page: 1, pageSize: 30, totalCount: 1, totalPages: 1 });
    TestBed.configureTestingModule({ providers: [
      { provide: LayawaysApi, useValue: api }, { provide: CustomersApi, useValue: customers }, { provide: CatalogApi, useValue: catalog },
      { provide: AuthorizationDialog, useValue: dialog }, { provide: SessionService, useValue: session },
    ] });
    component = TestBed.runInInjectionContext(() => new LayawayCreate());
    created = []; component.created.subscribe(x => created.push(x));
  });

  it('busca clientes activos y productos', async () => {
    component.customerSearch = 'Ana'; await component.findCustomer();
    expect(customers.list).toHaveBeenCalledWith({ search: 'Ana', status: 'active', page: 1, pageSize: 20 });
    component.productSearch = 'cuaderno'; await component.findProduct();
    expect(catalog.products).toHaveBeenCalledWith({ search: 'cuaderno', page: 1, pageSize: 30 });
    expect(component.results().length).toBe(1);
  });

  it('rechaza productos inactivos, agotados y mayorista sin precio', () => {
    component.add(product({ isActive: false })); expect(component.error()).toContain('inactivo o agotado');
    component.add(product({ availableStock: 0 })); expect(component.lines().length).toBe(0);
    component.add(product({ wholesalePrice: null }), 'MAYORISTA'); expect(component.error()).toContain('mayorista');
    expect(component.lines().length).toBe(0);
  });

  it('fusiona el mismo producto sin superar la existencia y bloquea otro tipo de precio', () => {
    component.add(product({ availableStock: 2 })); component.add(product({ availableStock: 2 })); component.add(product({ availableStock: 2 }));
    expect(component.lines()).toHaveLength(1);
    expect(component.lines()[0].quantity).toBe(2);
    component.add(product(), 'MAYORISTA');
    expect(component.error()).toContain('otro tipo de precio');
    expect(component.lines()[0].priceType).toBe('MINORISTA');
  });

  it('calcula el total con precios minorista y mayorista', () => {
    component.add(product()); component.add(product({ id: 'p2', retailPrice: 5000, wholesalePrice: 4000 }), 'MAYORISTA');
    component.lines()[0].quantity = 2; component.lines()[1].quantity = 3; component.update();
    expect(component.total()).toBe(2 * 10000 + 3 * 4000);
    component.setPriceType(component.lines()[1], 'MINORISTA');
    expect(component.total()).toBe(2 * 10000 + 3 * 5000);
  });

  it.each([
    ['sin cliente', () => { component.add(product()); }, 'cliente'],
    ['sin productos', () => { component.selectCustomer(customer); }, 'al menos un producto'],
    ['cantidad mayor que la existencia', () => { fill(); component.lines()[0].quantity = 6; component.update(); }, 'cantidades'],
    ['cantidad decimal', () => { fill(); component.lines()[0].quantity = 1.5; component.update(); }, 'cantidades'],
    ['pago inicial mayor que el total', () => { fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 10001, receivedAmount: 10001, reference: '' }]; }, 'supera el total'],
    ['electrónico sin referencia', () => { fill(); component.drafts = [{ methodCode: 'NEQUI', appliedAmount: 5000, receivedAmount: null, reference: '' }]; }, 'referencia'],
  ])('valida: %s', async (_name, setup, text) => {
    setup();
    expect(component.validation()).toContain(text);
    await component.confirm();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('pide autorización de otro usuario y envía el apartado con la clave', async () => {
    fill();
    await component.confirm();
    expect(prompt!.requireDifferentUser).toBe(true);
    expect(prompt!.operatorUserName).toBe('cajera');
    expect(prompt!.detail).toContain('Ana');
    expect(api.create).toHaveBeenCalledWith({
      customerId: 'c1', lines: [{ productId: 'p1', quantity: 1, priceType: 'MINORISTA' }],
      payments: [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 8000, reference: null }],
      dueDate: null, authorization: { userName: 'supervisor', password: 'secreto' },
    }, expect.any(String));
  });

  it('envía la fecha límite elegida', async () => {
    fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }]; component.dueDate = '2026-11-20';
    await component.confirm();
    expect(api.create.mock.lastCall![0].dueDate).toBe('2026-11-20');
  });

  it('emite el apartado creado y no conserva la clave tras el éxito', async () => {
    fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];
    await component.confirm();
    expect(created).toHaveLength(1);
    const first = api.create.mock.lastCall![1];
    await component.confirm();
    expect(api.create.mock.lastCall![1]).not.toBe(first);
  });

  it('reutiliza la clave ante un reintento idéntico y la cambia si cambia el contenido', async () => {
    fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];
    api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.confirm();
    expect(dialogError).toBeInstanceOf(HttpErrorResponse);
    const key = api.create.mock.lastCall![1];
    api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.confirm();
    expect(api.create.mock.lastCall![1]).toBe(key);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 3000, receivedAmount: 3000, reference: '' }];
    await component.confirm();
    expect(api.create.mock.lastCall![1]).not.toBe(key);
  });

  it('exige el mismo autorizador para reintentar el mismo contenido', async () => {
    fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];
    api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.confirm();
    credentials = { userName: 'otro', password: 'x' };
    await component.confirm();
    expect((dialogError as Error).message).toContain('mismo usuario autorizador');
    expect(api.create).toHaveBeenCalledTimes(1);
  });

  it('traduce el 403 a un mensaje de autorización sin reutilizar la clave', async () => {
    fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];
    api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    await component.confirm();
    expect((dialogError as Error).message).toContain('No se autorizó el apartado');
    const key = api.create.mock.lastCall![1];
    await component.confirm();
    expect(api.create.mock.lastCall![1]).not.toBe(key);
  });

  describe.each([['ADMINISTRADOR'], ['SUPERVISOR']])('%s sin segundo autorizador', role => {
    beforeEach(() => { state.roles = [role]; });

    it('crea sin abrir la ventana y omite el campo authorization', async () => {
      fill();
      await component.confirm();
      expect(dialog.open).not.toHaveBeenCalled();
      const request = api.create.mock.lastCall![0];
      expect('authorization' in request).toBe(false);
      expect(request).toEqual({
        customerId: 'c1', lines: [{ productId: 'p1', quantity: 1, priceType: 'MINORISTA' }],
        payments: [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 8000, reference: null }], dueDate: null,
      });
      expect(created).toHaveLength(1);
    });

    it('valida igual que los demás roles antes de enviar', async () => {
      component.add(product());
      await component.confirm();
      expect(api.create).not.toHaveBeenCalled();
      expect(component.error()).toContain('cliente');
    });

    it('muestra el error de la API en el formulario y reutiliza la clave al reintentar', async () => {
      fill();
      api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'No hay turno abierto.' } }));
      await component.confirm();
      expect(component.error()).toBe('No hay turno abierto.');
      const key = api.create.mock.lastCall![1];
      await component.confirm();
      expect(api.create.mock.lastCall![1]).toBe(key);
      expect(created).toHaveLength(1);
    });

    it('traduce el 403 a un mensaje de permiso y descarta la clave', async () => {
      fill();
      api.create.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
      await component.confirm();
      expect(component.error()).toBe('No tienes permiso para crear apartados.');
      const key = api.create.mock.lastCall![1];
      await component.confirm();
      expect(api.create.mock.lastCall![1]).not.toBe(key);
    });
  });

  it('CAJERO siempre pasa por la ventana de autorización', async () => {
    state.roles = ['CAJERO'];
    fill();
    await component.confirm();
    expect(dialog.open).toHaveBeenCalledOnce();
    expect(prompt!.requireDifferentUser).toBe(true);
    expect(api.create.mock.lastCall![0].authorization).toEqual({ userName: 'supervisor', password: 'secreto' });
  });
  describe('regla del pago inicial mínimo y plazo de la fecha límite', () => {
    const loaded = async () => { await Promise.resolve(); await Promise.resolve(); };

    it('calcula el mínimo como el servidor: techo del porcentaje, al menos un peso y nunca más que el total', () => {
      const on = { requireMinimumInitial: true, minimumPercentage: 20 };
      expect(minimumInitialPayment(10000, on)).toBe(2000);
      expect(minimumInitialPayment(1001, on)).toBe(201);
      expect(minimumInitialPayment(5, { requireMinimumInitial: true, minimumPercentage: 0.01 })).toBe(1);
      expect(minimumInitialPayment(1001, { requireMinimumInitial: true, minimumPercentage: 100 })).toBe(1001);
      expect(minimumInitialPayment(10000, { requireMinimumInitial: true, minimumPercentage: 33.33 })).toBe(3333);
      expect(minimumInitialPayment(10000, { requireMinimumInitial: false, minimumPercentage: 20 })).toBe(1);
      expect(minimumInitialPayment(0, on)).toBeNull(); expect(minimumInitialPayment(10000, null)).toBeNull();
    });

    it('muestra el mínimo en pesos según el total y rechaza un pago inicial menor', async () => {
      await loaded(); fill(); component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 1000, receivedAmount: 1000, reference: '' }];
      expect(component.rules()?.minimumPercentage).toBe(20); expect(component.minimumInitial()).toBe(2000);
      expect(component.validation()).toContain('al menos'); expect(component.validation()).toContain('2.000');
      component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];
      expect(component.validation()).toBe('');
    });

    it('con la exigencia apagada el mínimo es un peso', async () => {
      api.creationRules.mockResolvedValue({ requireMinimumInitial: false, minimumPercentage: 20, defaultDays: 15 });
      component = TestBed.runInInjectionContext(() => new LayawayCreate()); await loaded(); fill();
      expect(component.minimumInitial()).toBe(1);
      component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 1, receivedAmount: 1, reference: '' }];
      expect(component.validation()).toBe('');
    });

    it('muestra el plazo predeterminado con su fecha y aclara que se puede cambiar', async () => {
      await loaded();
      expect(component.defaultDue()).toEqual({ days: 15, date: dateInDays(15) }); expect(component.dueRequired).toBe(false);
      const fixture = TestBed.createComponent(LayawayCreate); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
      const text = (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('Plazo predeterminado: 15 días'); expect(text).toContain(dateInDays(15)); expect(text).toContain('elige otra fecha para cambiarlo');
      expect(text).toContain('opcional'); expect(text).toContain('al menos el 20 % del total');
    });

    it('sin plazo predeterminado la fecha es obligatoria', async () => {
      api.creationRules.mockResolvedValue({ requireMinimumInitial: true, minimumPercentage: 20, defaultDays: null });
      component = TestBed.runInInjectionContext(() => new LayawayCreate()); await loaded(); fill();
      expect(component.dueRequired).toBe(true); expect(component.defaultDue()).toBeNull();
      expect(component.validation()).toContain('fecha límite'); component.dueDate = '2026-12-01'; expect(component.validation()).toBe('');
      const fixture = TestBed.createComponent(LayawayCreate); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
      const text = (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('obligatoria'); expect(text).toContain('No hay un plazo predeterminado');
    });

    it('si no se pueden leer las reglas conserva el texto genérico y no bloquea (decide el servidor)', async () => {
      api.creationRules.mockRejectedValue(new HttpErrorResponse({ status: 403 }));
      component = TestBed.runInInjectionContext(() => new LayawayCreate()); await loaded(); fill();
      expect(component.rules()).toBeNull(); expect(component.minimumInitial()).toBeNull(); expect(component.dueRequired).toBe(false);
      expect(component.validation()).toBe('');
    });
  });
});
