import { TestBed } from '@angular/core/testing';
import { Catalog } from './catalog';
import { CatalogApi, Product } from './catalog-api';
import { SessionService } from '../../core/session.service';
import { HttpErrorResponse } from '@angular/common/http';

describe('Catalog: existencias iniciales', () => {
  let component: Catalog;
  const api = { createProduct: vi.fn(), products: vi.fn(), updateProduct: vi.fn(), units: vi.fn(), categories: vi.fn(), createCategory: vi.fn(), updateCategory: vi.fn() };
  const user = { id: 'actor-1', roles: ['SUPERVISOR'], permissions: ['PRODUCTS.CREATE', 'PRODUCTS.READ'] };
  const session = { has: (permission: string) => user.permissions.includes(permission), hasRole: (...codes: string[]) => codes.some(code => user.roles.includes(code)), user: () => user, refreshUser: vi.fn(async () => {}) };
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.removeItem('controlplus.catalog.create-attempt');
    user.roles = ['SUPERVISOR']; user.permissions = ['PRODUCTS.CREATE', 'PRODUCTS.READ'];
    api.products.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.createProduct.mockResolvedValue({ internalCode: 'P1' });
    api.updateProduct.mockResolvedValue({ internalCode: 'P-000001' });
    api.units.mockResolvedValue([{ id: 'unit', code: 'UNIDAD', name: 'Unidad', symbol: 'u', isActive: true }]);
    api.categories.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    TestBed.configureTestingModule({ providers: [
      { provide: CatalogApi, useValue: api }, { provide: SessionService, useValue: session },
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
      component.editProduct(product);
      expect(component.generateBarcode()).toBe(false);
      component.productForm.controls.name.setValue('Nombre editado');
      await component.saveProduct();
      expect(api.updateProduct).toHaveBeenCalledWith(product, expect.objectContaining({
        internalCode: 'P-000001', barcode, barcodeFormat: format,
      }));
      expect(api.createProduct).not.toHaveBeenCalled();
    });

  it('renderiza las columnas aprobadas, el alta predeterminada y la sección activa aunque cambie el foco', async () => {
    user.permissions.push('CATEGORIES.READ');
    const fixture = TestBed.createComponent(Catalog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(Array.from(root.querySelectorAll('table thead th')).slice(0, 3).map(x => x.textContent?.trim()))
      .toEqual(['N.º de producto', 'Código de barras', 'Producto']);
    fixture.componentInstance.newProduct();
    fixture.detectChanges();
    expect((root.querySelector('mat-checkbox input') as HTMLInputElement).checked).toBe(true);
    expect(root.querySelector('mat-select[formControlName="barcodeFormat"]')).toBeNull();
    expect(root.textContent).toContain('Costo de compra por unidad');
    expect(fixture.componentInstance.productForm.controls.measurementUnitId.value).toBe('unit');
    const categoryTab = Array.from(root.querySelectorAll<HTMLButtonElement>('.tabs button'))
      .find(button => button.textContent?.trim() === 'Categorías')!;
    categoryTab.click();
    await fixture.whenStable();
    fixture.detectChanges();
    categoryTab.focus(); categoryTab.blur();
    expect(categoryTab.classList.contains('active')).toBe(true);
    expect(getComputedStyle(categoryTab).color).toBe('rgb(255, 255, 255)');
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
    expect(api.products).toHaveBeenCalledWith(expect.objectContaining({ search: 'Producto', categoryId: 'category', includeOutOfStock: false, pageSize: 200 }));
    expect(component.createOutcomeUncertain()).toBe(true);
    expect(component.createErrorKind()).toBe('uncertain');
    expect(component.error()).toContain('No se puede conectar con la API local.');
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
    expect(component.filters.controls.includeOutOfStock.value).toBe(false);
    expect(component.notice()).toContain('PRD-1');
  });
});
