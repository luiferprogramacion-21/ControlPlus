import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CatalogApi, CreateProductInput, Product } from './catalog-api';

describe('CatalogApi: alta de producto', () => {
  let api: CatalogApi;
  let http: HttpTestingController;
  const input: CreateProductInput = {
    categoryId: 'category', measurementUnitId: 'unit', supplierId: null,
    barcode: null, barcodeFormat: null, name: 'Producto', description: null,
    retailPrice: 1000, wholesalePrice: null, taxPercentage: null, minimumStock: 2,
    initialQuantity: 5, initialUnitCost: 650.25,
  };
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CatalogApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('envía cantidad, costo, generación de código y clave de idempotencia', async () => {
    const task = api.createProduct(input, true, 'alta-123');
    const request = http.expectOne('/api/products');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('alta-123');
    expect(request.request.body).toEqual({ ...input, internalCode: null, generateBarcode: true });
    request.flush({ id: 'product' } as Product);
    expect((await task).id).toBe('product');
  });

  it('crea categorías enviando nombre, descripción y abreviatura', async () => {
    const task = api.createCategory('Papelería', null, 'PAP');
    const request = http.expectOne('/api/categories');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Papelería', description: null, abbreviation: 'PAP' });
    request.flush({ id: 'c', abbreviation: 'PAP' });
    expect((await task).abbreviation).toBe('PAP');
  });

  it('actualiza categorías enviando la abreviatura solo cuando existe y conservando la versión', async () => {
    const category = { id: 'c', name: 'Papelería', description: null, isActive: true, version: 4, abbreviation: null };
    const changed = api.updateCategory(category, 'Papelería', 'Útiles', 'PAP');
    const first = http.expectOne('/api/categories/c');
    expect(first.request.method).toBe('PUT');
    expect(first.request.body).toEqual({ name: 'Papelería', description: 'Útiles', version: 4, abbreviation: 'PAP' });
    first.flush({ ...category, abbreviation: 'PAP' });
    await changed;
    const kept = api.updateCategory(category, 'Papelería', null, null);
    const second = http.expectOne('/api/categories/c');
    expect('abbreviation' in second.request.body).toBe(false);
    second.flush(category);
    await kept;
  });

  it('conserva el alta sin existencias con costo desconocido', async () => {
    const task = api.createProduct({ ...input, initialQuantity: 0, initialUnitCost: null }, false, 'alta-cero');
    const request = http.expectOne('/api/products');
    expect(request.request.body).toMatchObject({ initialQuantity: 0, initialUnitCost: null, internalCode: null, generateBarcode: false });
    expect(request.request.headers.get('Idempotency-Key')).toBe('alta-cero');
    request.flush({ id: 'empty' } as Product);
    await task;
  });

  it('recupera un intento antiguo sin sustituir su código interno ni la clave original', async () => {
    const original = { ...input, internalCode: 'LEGACY-INITIAL-1' };
    const task = api.createProduct(original, false, 'clave-original');
    const request = http.expectOne('/api/products');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('clave-original');
    expect(request.request.body).toEqual({ ...original, generateBarcode: false });
    request.flush({ id: 'original-product' } as Product);
    expect((await task).id).toBe('original-product');
  });

  it('conserva metadatos externos conocidos para clientes compatibles sin inventar código interno', async () => {
    const external = api.createProduct({ ...input, barcode: '7501234567890', barcodeFormat: 'EAN13', initialQuantity: 0, initialUnitCost: null }, false, 'externo-1');
    const request = http.expectOne('/api/products');
    expect(request.request.body).toMatchObject({ barcode: '7501234567890', barcodeFormat: 'EAN13', generateBarcode: false });
    expect(request.request.body.internalCode).toBeNull();
    request.flush({ id: 'external-product' } as Product);
    await external;
  });

  it('envía un código externo con ceros iniciales y formato desconocido sin inferirlo', async () => {
    const task = api.createProduct({ ...input, barcode: '0000123456789', barcodeFormat: null }, false, 'externo-ceros');
    const request = http.expectOne('/api/products');
    expect(request.request.headers.get('Idempotency-Key')).toBe('externo-ceros');
    expect(request.request.body).toEqual({ ...input, barcode: '0000123456789', barcodeFormat: null,
      internalCode: null, generateBarcode: false });
    request.flush({ id: 'external-zeros' } as Product);
    expect((await task).id).toBe('external-zeros');
  });
});
