import { TestBed } from '@angular/core/testing';
import { Catalog } from './catalog';
import { CatalogApi, Product } from './catalog-api';
import { SessionService } from '../../core/session.service';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { InventoryApi } from '../inventory/inventory-api';
import { Router } from '@angular/router';

describe('Catalog: existencias iniciales', () => {
  let component: Catalog;
  const api = { createProduct: vi.fn(), products: vi.fn(), updateProduct: vi.fn(), units: vi.fn(), categories: vi.fn(), createCategory: vi.fn(), updateCategory: vi.fn(), summary: vi.fn(), costHistory: vi.fn(), activeSuppliers: vi.fn() };
  const inventory = { movements: vi.fn() };
  const router = { navigate: vi.fn(async () => true) };
  const user = { id: 'actor-1', roles: ['SUPERVISOR'], permissions: ['PRODUCTS.CREATE', 'PRODUCTS.READ'] };
  const session = { has: (permission: string) => user.permissions.includes(permission), hasRole: (...codes: string[]) => codes.some(code => user.roles.includes(code)), user: () => ({ ...user, userName: 'super' }), refreshUser: vi.fn(async () => {}) };
  // Decisión D5: el Supervisor edita con la autorización del Administrador (el diálogo se simula).
  const authorization = { open: vi.fn(async (prompt: AuthorizationPrompt) => { await prompt.run({ userName: 'admin', password: 'clave' }); return true; }) };
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.removeItem('controlplus.catalog.create-attempt');
    user.roles = ['SUPERVISOR']; user.permissions = ['PRODUCTS.CREATE', 'PRODUCTS.READ'];
    api.products.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.createProduct.mockResolvedValue({ internalCode: 'P1' });
    api.updateProduct.mockResolvedValue({ internalCode: 'P-000001' });
    api.units.mockResolvedValue([{ id: 'unit', code: 'UNIDAD', name: 'Unidad', symbol: 'u', isActive: true }]);
    api.categories.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.summary.mockResolvedValue({ active: 12, lowStock: 3, outOfStock: 2, withoutPrice: 1 });
    api.activeSuppliers.mockResolvedValue([]);
    TestBed.configureTestingModule({ providers: [
      { provide: CatalogApi, useValue: api }, { provide: SessionService, useValue: session },
      { provide: AuthorizationDialog, useValue: authorization }, { provide: InventoryApi, useValue: inventory },
      { provide: Router, useValue: router },
    ] });
    component = TestBed.runInInjectionContext(() => new Catalog());
    component.newProduct();
    component.productForm.patchValue({ name: 'Producto', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000 });
  });

  it('permite a Supervisor con PRODUCTS.CREATE dar de alta con existencia y costo sin INVENTORY.ADJUST', async () => {
    component.productForm.patchValue({ initialQuantity: 5, initialUnitCost: '650.2500' });
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ initialQuantity: 5, initialUnitCost: 650.25 }), true, expect.any(String));
  });

  it('permite a Administrador con PRODUCTS.CREATE y rechaza al revocar el permiso vigente', async () => {
    user.roles = ['ADMINISTRADOR'];
    component.productForm.patchValue({ initialQuantity: 3, initialUnitCost: '0' });
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    component.newProduct();
    component.productForm.patchValue({ name: 'Otro', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000, initialQuantity: 3, initialUnitCost: '0' });
    user.permissions = ['PRODUCTS.READ'];
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    expect(component.productForm.controls.name.value).toBe('Otro');
    expect(component.error()).toContain('permiso');
  });

  it('conserva todos los datos del formulario cuando la API rechaza el alta', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    component.productForm.patchValue({ initialQuantity: 5, initialUnitCost: '650.2500' });
    await component.saveProduct();
    expect(component.selectedProduct()?.id).toBe('');
    expect(component.productForm.getRawValue()).toMatchObject({ name: 'Producto', initialQuantity: 5, initialUnitCost: '650.2500' });
    expect(component.error()).toContain('permiso');
    expect(component.notice()).toBe('');
  });

  it('muestra la causa segura de validación, unicidad y conflicto de idempotencia', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { errors: { request: ['El costo inicial es obligatorio cuando hay existencias.'] } } }));
    component.productForm.patchValue({ initialQuantity: 1, initialUnitCost: '10' });
    await component.saveProduct();
    expect(component.createErrorKind()).toBe('validation');
    expect(component.error()).toContain('El costo inicial es obligatorio');

    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'Ya existe un producto con ese código de barras.', code: 'resource.conflict' } }));
    await component.saveProduct();
    expect(component.createErrorKind()).toBe('unique');
    expect(component.error()).toContain('código de barras');
    const originalKey = api.createProduct.mock.lastCall?.[2];

    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'La clave de idempotencia ya fue utilizada con otra solicitud.', code: 'resource.conflict' } }));
    await component.saveProduct();
    expect(component.createErrorKind()).toBe('idempotency');
    expect(api.createProduct.mock.lastCall?.[2]).toBe(originalKey);
    expect(component.productForm.controls.name.value).toBe('Producto');
    expect(component.notice()).toBe('');
  });

  it('conserva nulos al borrar los importes opcionales', async () => {
    component.productForm.patchValue({ wholesalePrice: null, initialUnitCost: null });
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ wholesalePrice: null, initialUnitCost: null, initialQuantity: 0 }), true, expect.any(String));
  });

  describe('calculadora de precio', () => {
    const product = (overrides: object = {}) => ({ id: '', version: 0, canViewCost: false, averageCost: null, ...overrides }) as never;
    it('al crear usa el costo de compra escrito y pone el precio minorista o el mayorista', () => {
      const p = product();
      expect(component.referenceCost(p)).toBeNull();
      component.productForm.patchValue({ initialUnitCost: '1234' });
      expect(component.referenceCost(p)).toBe(1234);
      component.setProfitPercent('25');
      component.applyProfitTo('retailPrice', p);
      expect(component.productForm.controls.retailPrice.value).toBe(1550);
      component.applyProfitTo('wholesalePrice', p);
      expect(component.productForm.controls.wholesalePrice.value).toBe(1550);
      expect(component.priceNote(p)?.text).toContain('ganas 25,6 %');
    });
    it('sin costo o sin porcentaje no cambia el precio y lo explica', () => {
      const p = product();
      component.setProfitPercent('30'); component.applyProfitTo('retailPrice', p);
      expect(component.productForm.controls.retailPrice.value).toBe(1000);
      expect(component.priceHint()).toContain('% de ganancia');
    });
    it('al editar usa el costo promedio solo si se puede ver', () => {
      expect(component.referenceCost(product({ id: 'p1', canViewCost: false, averageCost: 900 }))).toBeNull();
      expect(component.referenceCost(product({ id: 'p1', canViewCost: true, averageCost: 900 }))).toBe(900);
      component.productForm.patchValue({ retailPrice: 800 });
      expect(component.priceNote(product({ id: 'p1', canViewCost: true, averageCost: 900 }))?.low).toBe(true);
    });
  });

  describe('IVA del producto (interruptor Sin IVA / Con IVA)', () => {
    const existing = (taxPercentage: number | null) => ({
      id: 'p1', version: 1, name: 'Producto', internalCode: 'P-000001', categoryId: 'category', measurementUnitId: 'unit', barcode: null,
      supplierId: null, barcodeFormat: null, description: null, retailPrice: 1000, wholesalePrice: null, taxPercentage, minimumStock: 0,
    }) as unknown as Product;

    it('por defecto es Sin IVA: no pide porcentaje y envía nulo', async () => {
      expect(component.hasTax()).toBe(false);
      await component.saveProduct();
      expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ taxPercentage: null }), true, expect.any(String));
    });
    it('el IVA se quitó del sistema: aunque quede un porcentaje guardado, envía nulo', async () => {
      component.productForm.patchValue({ taxPercentage: '19' });
      await component.saveProduct();
      expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ taxPercentage: null }), true, expect.any(String));
    });
    it('rechaza un porcentaje fuera de 0 a 100', () => {
      component.setHasTax(true);
      component.productForm.patchValue({ taxPercentage: '101' });
      expect(component.productForm.controls.taxPercentage.invalid).toBe(true);
      component.productForm.patchValue({ taxPercentage: '19.5' });
      expect(component.productForm.controls.taxPercentage.valid).toBe(true);
    });
    it('apagar el interruptor borra el porcentaje y no lo envía', async () => {
      component.setHasTax(true);
      component.productForm.patchValue({ taxPercentage: '19' });
      component.setHasTax(false);
      expect(component.productForm.controls.taxPercentage.value).toBe('');
      await component.saveProduct();
      expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ taxPercentage: null }), true, expect.any(String));
    });
    it('cada alta nueva vuelve a Sin IVA', () => {
      component.setHasTax(true);
      component.productForm.patchValue({ taxPercentage: '19' });
      component.newProduct();
      expect(component.hasTax()).toBe(false);
      expect(component.productForm.controls.taxPercentage.value).toBe('');
    });
    it('al editar el interruptor refleja si el producto tiene IVA guardado', () => {
      component.editProduct(existing(19));
      expect(component.hasTax()).toBe(true);
      expect(component.productForm.controls.taxPercentage.value).toBe('19');
      component.editProduct(existing(null));
      expect(component.hasTax()).toBe(false);
      expect(component.productForm.controls.taxPercentage.value).toBe('');
    });
  });

  it('conserva el alta sin existencias', async () => {
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ initialQuantity: 0, initialUnitCost: null }), true, expect.any(String));
  });

  it('activa generar código en cada alta nueva y envía barras externas sin inferir formato ni perder ceros', async () => {
    expect(component.generateBarcode()).toBe(true);
    component.generateBarcode.set(false);
    component.productForm.controls.barcode.setValue('0000123456789');
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledWith(expect.objectContaining({ barcode: '0000123456789', barcodeFormat: null }), false, expect.any(String));
    component.newProduct();
    expect(component.generateBarcode()).toBe(true);
    expect(component.productForm.controls.barcode.value).toBe('');
  });

  it.each([[false, '0000123456789', 'EAN13'], [true, 'P-000001', 'CODE128']])(
    'al editar conserva barras y metadatos, con generado=%s', async (generated, barcode, format) => {
      const product = { id: 'product', internalCode: 'P-000001', version: 1, name: 'Original',
        categoryId: 'category', measurementUnitId: 'unit', supplierId: null, barcode, barcodeFormat: format,
        barcodeGenerated: generated, retailPrice: 1000, wholesalePrice: null, taxPercentage: null,
        minimumStock: 0, description: null } as Product;
      user.permissions.push('PRODUCTS.SENSITIVE_UPDATE');
      component.editProduct(product);
      expect(component.generateBarcode()).toBe(false);
      component.productForm.controls.name.setValue('Nombre editado');
      await component.saveProduct();
      expect(api.updateProduct).toHaveBeenCalledWith(product, expect.objectContaining({
        internalCode: 'P-000001', barcode, barcodeFormat: format,
      }));
      expect(api.createProduct).not.toHaveBeenCalled();
    });

  it('el Supervisor cambia el precio con la autorización del Administrador (D5)', async () => {
    const product = { id: 'product', internalCode: 'P-000001', version: 1, name: 'Original',
      categoryId: 'category', measurementUnitId: 'unit', supplierId: null, barcode: null, barcodeFormat: null,
      barcodeGenerated: false, retailPrice: 1000, wholesalePrice: null, taxPercentage: null,
      minimumStock: 0, description: null } as unknown as Product;
    expect(component.canEditProduct()).toBe(true);
    expect(component.productEditNeedsAuthorization()).toBe(true);
    component.editProduct(product);
    component.productForm.controls.retailPrice.setValue(1500 as never);
    await component.saveProduct();
    expect(authorization.open).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Autorizar cambio de producto', operatorUserName: 'super', requireDifferentUser: true, allowCredential: true,
      detail: expect.stringContaining('1.000 → 1.500'),
    }));
    expect(api.updateProduct).toHaveBeenCalledWith(product, expect.objectContaining({ retailPrice: 1500 }),
      { userName: 'admin', password: 'clave' });
    user.roles = ['CAJERO']; user.permissions = ['PRODUCTS.READ'];
    expect(component.canEditProduct()).toBe(false);
  });

  it('un producto con precio mayorista se edita: el precio llega como número y Guardar pide la autorización (punto 5)', async () => {
    const product = { id: 'product', internalCode: 'BIS-000016', version: 3, name: 'Aretes pequeños (unidad)',
      categoryId: 'category', measurementUnitId: 'unit', supplierId: null, barcode: 'CP000016', barcodeFormat: 'CODE128',
      barcodeGenerated: true, retailPrice: 12000, wholesalePrice: 7000, taxPercentage: null,
      minimumStock: 10, description: null } as unknown as Product;
    component.editProduct(product);
    expect(component.productForm.controls.wholesalePrice.value).toBe(7000);
    expect(component.productForm.valid).toBe(true);
    component.productForm.controls.retailPrice.setValue(12500 as never);
    await component.saveProduct();
    expect(authorization.open).toHaveBeenCalledWith(expect.objectContaining({ title: 'Autorizar cambio de producto' }));
    expect(api.updateProduct).toHaveBeenCalledWith(product, expect.objectContaining({ retailPrice: 12500, wholesalePrice: 7000 }),
      { userName: 'admin', password: 'clave' });
  });

  it('si el formulario tiene un error, Guardar avisa qué revisar en vez de quedarse callado', async () => {
    const product = { id: 'product', internalCode: 'P-000001', version: 1, name: 'Original',
      categoryId: 'category', measurementUnitId: 'unit', supplierId: null, barcode: null, barcodeFormat: null,
      barcodeGenerated: false, retailPrice: 1000, wholesalePrice: null, taxPercentage: null,
      minimumStock: 0, description: null } as unknown as Product;
    component.editProduct(product);
    component.productForm.controls.name.setValue('');
    await component.saveProduct();
    expect(authorization.open).not.toHaveBeenCalled();
    expect(component.error()).toBe('Revisa los campos marcados en rojo y vuelve a guardar.');
  });

  it('muestra Disponible, Stock bajo o Agotado y filtra por existencias (auditoría C.19)', async () => {
    const base = { isActive: true, minimumStock: 5 };
    expect(component.stockState({ ...base, availableStock: 0 }).label).toBe('Agotado');
    expect(component.stockState({ ...base, availableStock: 5 }).label).toBe('Stock bajo');
    expect(component.stockState({ ...base, availableStock: 6 }).label).toBe('Disponible');
    expect(component.stockState({ ...base, isActive: false, availableStock: 9 }).label).toBe('Inactivo');
    component.setProductFilter('AGOTADO');
    await component.reload();
    expect(api.products).toHaveBeenLastCalledWith(expect.objectContaining({ stockStatus: 'AGOTADO' }));
    component.setProductFilter('');
    await component.reload();
    expect(api.products).toHaveBeenLastCalledWith(expect.objectContaining({ stockStatus: undefined }));
  });

  describe('maqueta 11 (aprobada el 26/11/2026)', () => {
    const product = (extra: Partial<Product>): Product => ({ id: 'p1', internalCode: 'PAP-000004', barcode: 'CP000016', name: 'Cuaderno', categoryName: 'Papelería',
      categoryId: 'c', measurementUnitId: 'unit', measurementUnitCode: 'UNIDAD', supplierId: 's1', retailPrice: 3000, wholesalePrice: null, averageCost: null,
      canViewCost: false, currentStock: 24, reservedStock: 0, availableStock: 24, minimumStock: 5, isActive: true, version: 1, ...extra } as Product);

    it('los grupos piden al servidor sin precio, solo inactivos o todos con agotados', async () => {
      user.permissions.push('INVENTORY.READ', 'PRODUCTS.SENSITIVE_UPDATE');
      component.setProductFilter('SIN_PRECIO'); await component.reload();
      expect(api.products).toHaveBeenLastCalledWith(expect.objectContaining({ withoutPrice: true, stockStatus: undefined }));
      component.setProductFilter('INACTIVOS'); await component.reload();
      expect(api.products).toHaveBeenLastCalledWith(expect.objectContaining({ onlyInactive: true }));
      component.setProductFilter(''); await component.reload();
      expect(api.products).toHaveBeenLastCalledWith(expect.objectContaining({ includeOutOfStock: true, withoutPrice: undefined, onlyInactive: undefined }));
      expect(component.summary()).toEqual({ active: 12, lowStock: 3, outOfStock: 2, withoutPrice: 1 });
      component.toggleSummaryFilter('STOCK_BAJO'); expect(component.productFilter()).toBe('STOCK_BAJO');
      component.toggleSummaryFilter('STOCK_BAJO'); expect(component.productFilter()).toBe('');
      expect(component.productChips.map(chip => chip.label)).toContain('Inactivos');
    });

    it('la fila dice la categoría, el estado y qué falta', () => {
      expect(component.prefix(product({}))).toBe('PAP');
      expect(component.prefix(product({ internalCode: 'P-000001' }))).toBe('P');
      expect(component.stockText(product({}))).toBe('24 disponibles · mín. 5');
      expect(component.stockText(product({ availableStock: 3 }))).toBe('Stock bajo · 3 de mín. 5');
      expect(component.stockText(product({ availableStock: 0 }))).toBe('Agotado');
      expect(component.stockText(product({ retailPrice: 0, currentStock: 0, availableStock: 0 }))).toBe('Sin existencias todavía');
      expect(component.rowTone(product({ retailPrice: 0, currentStock: 0, availableStock: 0 }))).toBe('neutral');
      expect(component.stockText(product({ isActive: false }))).toBe('Inactivo');
      expect(component.detailLine(product({}))).toBe('PAP-000004 · barras CP000016 · Papelería');
    });

    it('«Pedir» abre el pedido con el proveedor y el producto; solo Supervisor o Administrador que hacen pedidos', () => {
      const low = product({ availableStock: 2 });
      expect(component.needsOrder(low)).toBe(true);
      expect(component.canOrder(low)).toBe(false);
      user.permissions.push('PURCHASE_ORDERS.MANAGE', 'SUPPLIERS.READ', 'INVENTORY.READ');
      expect(component.canOrder(low)).toBe(true);
      expect(component.canOrder(product({ supplierId: null }))).toBe(false);
      component.orderProduct(low);
      expect(router.navigate).toHaveBeenCalledWith(['/operacion/compras'], { queryParams: { accion: 'pedir', proveedor: 's1', producto: 'p1' } });
      expect(component.needsOrder(product({ availableStock: 0, retailPrice: 0 }))).toBe(false);
    });

    it('la ficha carga el historial y los costos al abrir su pestaña', async () => {
      user.permissions.push('INVENTORY.READ', 'PRODUCTS.COSTS_READ');
      inventory.movements.mockResolvedValue({ items: [{ id: 'm1', type: 'COMPRA', stockQuantity: 10, newStock: 24 }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
      api.costHistory.mockResolvedValue({ currentAverageCost: 1500, lastConfirmedPurchaseCost: 1500, history: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 } });
      component.openFicha(product({}));
      expect(component.fichaTab()).toBe('datos');
      await component.setFichaTab('existencias');
      expect(inventory.movements).toHaveBeenCalledWith({ productId: 'p1', page: 1, pageSize: 20 });
      expect(component.movements()?.items.length).toBe(1);
      await component.setFichaTab('costos');
      expect(api.costHistory).toHaveBeenCalledWith('p1', 1);
      component.closeFicha();
      expect(component.ficha()).toBeNull(); expect(component.movements()).toBeNull(); expect(component.costHistory()).toBeNull();
    });
  });

  it('renderiza las columnas aprobadas, el alta predeterminada y la sección activa aunque cambie el foco', async () => {
    user.permissions.push('CATEGORIES.READ');
    const fixture = TestBed.createComponent(Catalog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    // Maqueta 11: una fila por producto con su estado, precio y acciones, y el resumen arriba.
    expect(Array.from(root.querySelectorAll('table thead th')).map(x => x.textContent?.trim()))
      .toEqual(['Producto', 'Existencias', 'Precio', 'Acciones']);
    expect(root.querySelector('.catalog-summary')?.textContent).toContain('Sin precio (no se venden)');
    expect(Array.from(root.querySelectorAll('.catalog-chip')).map(x => x.textContent?.trim()))
      .toEqual(['Todos', 'Disponibles', 'Stock bajo', 'Agotados', 'Sin precio']);
    fixture.componentInstance.newProduct();
    fixture.detectChanges();
    // Propuesta 2 (21 de noviembre de 2026): unidad, barras, descripción y mayorista quedan en «Más datos».
    expect(root.querySelector('mat-checkbox input')).toBeNull();
    expect(root.textContent).toContain('Stock mínimo');
    (root.querySelector('.more-data-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((root.querySelector('mat-checkbox input') as HTMLInputElement).checked).toBe(true);
    expect(root.querySelector('mat-select[formControlName="barcodeFormat"]')).toBeNull();
    expect(root.textContent).toContain('Costo de compra por unidad');
    expect(fixture.componentInstance.productForm.controls.measurementUnitId.value).toBe('unit');
    fixture.componentInstance.cancel();
    fixture.detectChanges();
    const categoriesButton = Array.from(root.querySelectorAll<HTMLButtonElement>('.catalog__actions button'))
      .find(button => button.textContent?.trim() === 'Categorías')!;
    categoriesButton.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.tab()).toBe('categories');
    expect(root.querySelector('.catalog__header h2')?.textContent).toBe('Categorías');
    expect(Array.from(root.querySelectorAll<HTMLButtonElement>('.catalog__actions button')).map(x => x.textContent?.trim())).toEqual(['← Productos']);
  });

  describe('abreviatura de categoría', () => {
    const category = { id: 'cat', name: 'Papelería', description: null, isActive: true, version: 3, abbreviation: 'PAP' };
    beforeEach(() => { user.permissions.push('CATEGORIES.READ', 'CATEGORIES.MANAGE'); });

    it('exige la abreviatura al crear y la envía en mayúsculas', async () => {
      api.createCategory.mockResolvedValue(category);
      component.newCategory();
      component.categoryForm.patchValue({ name: 'Juguetes' });
      await component.saveCategory();
      expect(api.createCategory).not.toHaveBeenCalled();
      expect(component.categoryForm.controls.abbreviation.hasError('required')).toBe(true);
      component.categoryForm.patchValue({ abbreviation: 'jug' });
      await component.saveCategory();
      expect(api.createCategory).toHaveBeenCalledWith('Juguetes', null, 'JUG');
    });

    it.each(['A', 'J1G', 'ñandú', 'ABCDEFGHIJK', 'AB CD'])('rechaza la abreviatura inválida %s', async value => {
      component.newCategory();
      component.categoryForm.patchValue({ name: 'Juguetes', abbreviation: value });
      await component.saveCategory();
      expect(api.createCategory).not.toHaveBeenCalled();
      expect(component.categoryForm.controls.abbreviation.invalid).toBe(true);
    });

    it('edita una categoría con abreviatura enviando la abreviatura vigente', async () => {
      api.updateCategory.mockResolvedValue(category);
      component.editCategory(category);
      expect(component.categoryForm.controls.abbreviation.value).toBe('PAP');
      component.categoryForm.patchValue({ abbreviation: 'pap2' });
      await component.saveCategory();
      expect(api.updateCategory).not.toHaveBeenCalled();
      component.categoryForm.patchValue({ abbreviation: 'papel' });
      await component.saveCategory();
      expect(api.updateCategory).toHaveBeenCalledWith(category, 'Papelería', null, 'PAPEL');
    });

    it('permite guardar una categoría histórica sin abreviatura y no la ofrece para altas', async () => {
      const legacy = { ...category, abbreviation: null };
      api.updateCategory.mockResolvedValue(legacy);
      component.editCategory(legacy);
      await component.saveCategory();
      expect(api.updateCategory).toHaveBeenCalledWith(legacy, 'Papelería', null, null);
      expect(component.canCreateProductIn(legacy)).toBe(false);
      expect(component.canCreateProductIn(category)).toBe(true);
    });

    it('muestra la abreviatura y marca las categorías sin configurar en el listado y en el alta', async () => {
      api.categories.mockResolvedValue({ items: [category, { ...category, id: 'old', name: 'Antigua', abbreviation: null }], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 });
      const fixture = TestBed.createComponent(Catalog);
      fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
      const instance = fixture.componentInstance;
      await vi.waitFor(() => expect(instance.categoryOptions().length).toBe(2));
      instance.newProduct(); fixture.detectChanges();
      expect(instance.hasCategoriesWithoutAbbreviation()).toBe(true);
      expect(fixture.nativeElement.textContent).toContain('Las categorías sin prefijo se configuran');
      instance.switchTab('categories'); await fixture.whenStable(); fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent!;
      expect(text).toContain('Prefijo'); expect(text).toContain('PAP'); expect(text).toContain('Sin configurar');
    });
  });

  it.each([
    [ -1, '1' ], [1.5, '1'], [2147483648, '1'], [1, ''], [1, '-1'], [1, '1.00001'], [1, '100000000000000'], [0, '1'],
  ])('rechaza cantidad %s y costo %s', async (quantity, cost) => {
    component.productForm.patchValue({ initialQuantity: quantity, initialUnitCost: cost });
    await component.saveProduct();
    expect(api.createProduct).not.toHaveBeenCalled();
  });

  it('impide existencia inicial a Cajero o sin permiso efectivo', async () => {
    component.productForm.patchValue({ initialQuantity: 1, initialUnitCost: '0' });
    user.roles = ['CAJERO'];
    await component.saveProduct();
    expect(api.createProduct).not.toHaveBeenCalled();
    user.roles = ['SUPERVISOR']; user.permissions = ['PRODUCTS.READ'];
    await component.saveProduct();
    expect(api.createProduct).not.toHaveBeenCalled();
  });

  it('bloquea el doble envío y reintenta con la misma clave tras una respuesta perdida', async () => {
    let resolve!: (value: { internalCode: string }) => void;
    api.createProduct.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    component.productForm.patchValue({ initialQuantity: 2, initialUnitCost: '10' });
    const first = component.saveProduct();
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    resolve({ internalCode: 'P1' });
    await first;

    component.newProduct();
    component.productForm.patchValue({ name: 'Producto', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000, initialQuantity: 2, initialUnitCost: '10' });
    api.createProduct.mockRejectedValueOnce(new Error('Respuesta perdida'));
    await component.saveProduct();
    const failedKey = api.createProduct.mock.lastCall?.[2];
    component.newProduct();
    component.productForm.patchValue({ name: 'Producto', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000, initialQuantity: 2, initialUnitCost: '10' });
    await component.recoverCreate();
    expect(api.createProduct.mock.lastCall?.[2]).toBe(failedKey);
    component.newProduct();
  });

  it('bloquea cambios tras una respuesta incierta y recupera solo el contenido y la clave originales, aunque no haya coincidencias', async () => {
    component.productForm.patchValue({ initialQuantity: 2, initialUnitCost: '10' });
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.saveProduct();
    const originalCall = [...api.createProduct.mock.calls[0]];
    const originalAttempt = JSON.parse(sessionStorage.getItem('controlplus.catalog.create-attempt')!);
    await vi.waitFor(() => expect(component.reconcilingCreate()).toBe(false));
    expect(component.reconciliationCandidates()).toEqual([]);
    expect(component.createOutcomeUncertain()).toBe(true);

    component.productForm.patchValue({ name: 'Producto modificado', initialQuantity: 7, initialUnitCost: '25' });
    component.generateBarcode.set(true);
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    expect(component.productForm.controls.name.value).toBe('Producto modificado');
    expect(JSON.parse(sessionStorage.getItem('controlplus.catalog.create-attempt')!)).toEqual(originalAttempt);

    let resolve!: (value: { internalCode: string }) => void;
    api.createProduct.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    const recovery = component.recoverCreate();
    await vi.waitFor(() => expect(api.createProduct).toHaveBeenCalledTimes(2));
    await component.recoverCreate();
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledTimes(2);
    expect(api.createProduct.mock.calls[1]).toEqual(originalCall);
    expect(component.saving()).toBe(true);
    resolve({ internalCode: 'PRD-RECUPERADO' });
    await recovery;
    expect(component.createOutcomeUncertain()).toBe(false);
    expect(sessionStorage.getItem('controlplus.catalog.create-attempt')).toBeNull();

    component.newProduct();
    component.productForm.patchValue({ name: 'Alta deliberada', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000 });
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledTimes(3);
    expect(api.createProduct.mock.lastCall?.[2]).not.toBe(originalCall[2]);
  });

  it('conserva el alta incierta al cerrar, reabrir o reconstruir el componente; un rechazo de recuperación no la resuelve', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 503 }));
    await component.saveProduct();
    const originalCall = [...api.createProduct.mock.calls[0]];
    component.cancel();
    component.newProduct();
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(component.productForm.controls.name.value).toBe('Producto');
    component.productForm.controls.name.setValue('No debe crearse');
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();

    component = TestBed.runInInjectionContext(() => new Catalog());
    component.newProduct();
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(component.productForm.controls.name.value).toBe('Producto');
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    await component.recoverCreate();
    expect(api.createProduct.mock.lastCall).toEqual(originalCall);
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(JSON.parse(sessionStorage.getItem('controlplus.catalog.create-attempt')!).state).toBe('pending');
    component.productForm.controls.name.setValue('Tampoco debe crearse');
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledTimes(2);
  });

  it('un 409 genérico durante recuperación conserva el intento antiguo completo y bloquea otra clave', async () => {
    const original = {
      categoryId: 'category', measurementUnitId: 'unit', supplierId: null, internalCode: 'LEGACY-INITIAL-1',
      barcode: null, barcodeFormat: null, name: 'bolso grande', description: null,
      retailPrice: 1000, wholesalePrice: null, taxPercentage: null, minimumStock: 2,
      initialQuantity: 5, initialUnitCost: 650.25, generateBarcode: false,
    };
    const attempt = { actorId: user.id, key: 'clave-original', body: JSON.stringify(original) };
    sessionStorage.setItem('controlplus.catalog.create-attempt', JSON.stringify(attempt));
    component = TestBed.runInInjectionContext(() => new Catalog());
    component.newProduct();
    expect(api.createProduct).not.toHaveBeenCalled();
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: {
      code: 'resource.conflict', title: 'La operación entra en conflicto con el estado actual del recurso.',
    } }));
    await component.recoverCreate();
    const { generateBarcode, ...originalInput } = original;
    expect(api.createProduct).toHaveBeenCalledWith(originalInput, generateBarcode, attempt.key);
    expect(component.createErrorKind()).toBe('conflict');
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(component.notice()).toBe('');
    expect(JSON.parse(sessionStorage.getItem('controlplus.catalog.create-attempt')!)).toEqual({ ...attempt, state: 'pending' });
    component.productForm.controls.name.setValue('Otra alta');
    await component.saveProduct();
    expect(api.createProduct).toHaveBeenCalledOnce();
    expect(JSON.parse(sessionStorage.getItem('controlplus.catalog.create-attempt')!).body).toBe(attempt.body);
  });

  it('concilia una respuesta incierta con lectura incluyendo productos agotados y no confirma ausencia', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    const match = { id: 'possible', name: 'Producto', categoryId: 'category', measurementUnitId: 'unit', internalCode: 'P-1', barcode: null };
    api.products.mockResolvedValueOnce({ items: [match], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 });
    component.productForm.patchValue({ initialQuantity: 0 });
    await component.saveProduct();
    await vi.waitFor(() => expect(component.reconciliationCandidates()).toEqual([match]));
    expect(api.products).toHaveBeenCalledWith(expect.objectContaining({ search: 'Producto', categoryId: 'category', pageSize: 200 }));
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(component.createErrorKind()).toBe('uncertain');
    expect(component.error()).toContain('No se puede conectar con ControlPlus.');
    expect(api.createProduct).toHaveBeenCalledOnce();
  });

  it('si falla la lectura de conciliación no afirma que el producto falte', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    api.products.mockRejectedValueOnce(new HttpErrorResponse({ status: 403, error: { title: 'No se pudo consultar el catálogo.' } }));
    await component.saveProduct();
    await vi.waitFor(() => expect(component.reconciliationQueryFailed()).toBe(true));
    expect(component.reconciliationCandidates()).toEqual([]);
    expect(component.error()).toContain('La consulta no confirma si el alta se guardó.');
    expect(component.error()).not.toContain('Sin coincidencias');
    expect(api.createProduct).toHaveBeenCalledOnce();
  });

  it('usa valores iniciales y Unidad por defecto en alta nueva, y conserva la unidad al editar', () => {
    component.units.set([{ id: 'unit-default', code: 'UNIDAD', name: 'Unidad', symbol: 'und', isActive: true }]);
    component.newProduct();
    expect(component.productForm.controls.measurementUnitId.value).toBe('unit-default');
    expect(component.productForm.getRawValue()).toMatchObject({ retailPrice: 0, minimumStock: 0, initialQuantity: 0, initialUnitCost: '', barcodeFormat: '' });
    component.editProduct({ id: 'product', version: 1, measurementUnitId: 'unit-metro', name: 'Metro', internalCode: 'P1',
      categoryId: 'category', supplierId: null, barcode: null, barcodeFormat: null, description: null,
      retailPrice: 100, wholesalePrice: null, taxPercentage: null, minimumStock: 0, barcodeGenerated: false,
    } as Product);
    expect(component.productForm.controls.measurementUnitId.value).toBe('unit-metro');
  });

  it('conserva la clave para el mismo contenido y genera otra si cambia el alta tras un rechazo', async () => {
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { title: 'Datos inválidos.' } }));
    await component.saveProduct();
    const oldKey = api.createProduct.mock.lastCall?.[2];
    component.newProduct();
    component.productForm.patchValue({ name: 'Producto', categoryId: 'category', measurementUnitId: 'unit', retailPrice: 1000 });
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { title: 'Datos inválidos.' } }));
    await component.saveProduct();
    expect(api.createProduct.mock.lastCall?.[2]).toBe(oldKey);

    component.productForm.controls.name.setValue('Otro producto');
    api.createProduct.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { title: 'Datos inválidos.' } }));
    await component.saveProduct();
    expect(api.createProduct.mock.lastCall?.[2]).not.toBe(oldKey);
  });

  it('al guardar muestra el creado por su código aunque hubiera un filtro anterior', async () => {
    api.createProduct.mockResolvedValueOnce({ id: 'created', internalCode: 'PRD-1', name: 'Producto' });
    component.filters.controls.productSearch.setValue('búsqueda anterior');
    await component.saveProduct();
    expect(component.filters.controls.productSearch.value).toBe('PRD-1');
    expect(component.notice()).toContain('PRD-1');
  });

  describe('borrado de productos', () => {
    const product = { id: 'p1', internalCode: 'P-000001', name: 'Camisa', version: 3 } as Product;
    beforeEach(() => { Object.assign(api, { deleteProduct: vi.fn() }); });
    it('solo el Administrador con PRODUCTS.SENSITIVE_UPDATE ve el borrado', () => {
      user.roles = ['SUPERVISOR']; user.permissions = ['PRODUCTS.SENSITIVE_UPDATE']; expect(component.canDeleteProduct()).toBe(false);
      user.roles = ['ADMINISTRADOR']; user.permissions = []; expect(component.canDeleteProduct()).toBe(false);
      user.permissions = ['PRODUCTS.SENSITIVE_UPDATE']; expect(component.canDeleteProduct()).toBe(true);
    });
    it('pide confirmación y borra con el producto elegido', async () => {
      (api as any).deleteProduct.mockResolvedValue(undefined);
      component.askDelete(product); expect(component.deleting()).toBe(product); expect((api as any).deleteProduct).not.toHaveBeenCalled();
      await component.confirmDelete();
      expect((api as any).deleteProduct).toHaveBeenCalledWith(product);
      expect(component.deleting()).toBeNull(); expect(component.notice()).toContain('P-000001');
    });
    it('un producto con historial se explica y sugiere inactivar', async () => {
      (api as any).deleteProduct.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'resource.conflict' } }));
      component.askDelete(product); await component.confirmDelete();
      expect(component.error()).toContain('historial'); expect(component.error()).toContain('inactivarlo');
    });
    it('un conflicto de versión pide actualizar y recarga la lista', async () => {
      (api as any).deleteProduct.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'concurrency.conflict' } }));
      component.askDelete(product); await component.confirmDelete();
      expect(component.error()).toContain('cambió'); expect(api.products).toHaveBeenCalled();
    });
  });
});
