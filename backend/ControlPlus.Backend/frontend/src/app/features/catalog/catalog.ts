import { Component, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatOptionModule } from '@angular/material/core';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { RouterLink } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { errorText, Page } from '../../core/api';
import { profitPercentOf, suggestedPrice } from '../../shared/price-calculator';
import { CatalogApi, Category, CostHistory, CreateProductInput, Product, ProductInput, Supplier, Unit } from './catalog-api';
import { toastSignal } from '../../core/toast';

interface ProductCreateAttempt {
  actorId: string;
  body: string;
  key: string;
  state: 'pending' | 'rejected';
}

@Component({
  selector: 'cp-catalog',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatOptionModule, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule, MatPaginatorModule],
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(CatalogApi);
  readonly session = inject(SessionService);
  readonly tab = signal<'products' | 'categories'>('products');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly checkingCreate = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly createErrorKind = signal<'validation' | 'unique' | 'idempotency' | 'conflict' | 'uncertain' | null>(null);
  readonly createOutcomeUncertain = signal(false);
  readonly reconcilingCreate = signal(false);
  readonly reconciliationQueryFailed = signal(false);
  readonly reconciliationCandidates = signal<Product[]>([]);
  readonly products = signal<Page<Product> | null>(null);
  readonly categories = signal<Page<Category> | null>(null);
  readonly units = signal<Unit[]>([]);
  readonly suppliers = signal<Supplier[]>([]);
  readonly selectedProduct = signal<Product | null>(null);
  readonly selectedCategory = signal<Category | null>(null);
  readonly costHistory = signal<CostHistory | null>(null);
  readonly costProductName = signal('');
  readonly costProduct = signal<Product | null>(null);
  readonly productPage = signal(1);
  readonly categoryPage = signal(1);
  readonly productPageSize = signal(20);
  readonly categoryPageSize = signal(20);
  readonly generateBarcode = signal(false);
  /** Product IVA switch: «Sin IVA» (default) sends no percentage; «Con IVA» asks for it. Optional information. */
  readonly hasTax = signal(false);
  private requestId = 0;
  private createAttempt: ProductCreateAttempt | null = null;
  private readonly createAttemptStorageKey = 'controlplus.catalog.create-attempt';

  readonly filters = this.fb.nonNullable.group({
    productSearch: [''], categorySearch: [''], categoryId: [''],
    includeInactiveProducts: [false], includeInactiveCategories: [false],
  });
  readonly categoryForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', Validators.maxLength(500)],
    abbreviation: ['', Validators.pattern(/^[A-Za-z]{2,10}$/)],
  });
  readonly productForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    internalCode: [''],
    categoryId: ['', Validators.required],
    measurementUnitId: ['', Validators.required],
    supplierId: [''],
    barcode: [''], barcodeFormat: [''], description: [''],
    retailPrice: [0, [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)]],
    wholesalePrice: ['', Validators.pattern(/^\d*$/)],
    taxPercentage: ['', [Validators.min(0), Validators.max(100)]],
    minimumStock: [0, [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)]],
    initialQuantity: [0, [Validators.required, Validators.min(0), Validators.max(2147483647), Validators.pattern(/^\d+$/)]],
    initialUnitCost: ['', Validators.pattern(/^\d{1,14}(?:\.\d{1,4})?$/)],
  });

  // --- calculadora de precio (decisión del 18 de noviembre de 2026) -------------------------------
  readonly profitPercent = signal<number | null>(null);
  readonly priceHint = toastSignal('error');
  setProfitPercent(value: string): void { this.profitPercent.set(value === '' || !Number.isFinite(Number(value)) ? null : Number(value)); this.priceHint.set(''); }
  /** Costo con el que se calcula: el de compra que se escribe al crear, o el promedio actual si se puede ver. */
  referenceCost(product: Product): number | null {
    if (!product.id) { const value = Number(this.productForm.controls.initialUnitCost.value); return Number.isFinite(value) && value > 0 ? value : null; }
    return product.canViewCost && product.averageCost != null && product.averageCost > 0 ? product.averageCost : null;
  }
  costLabel(product: Product): string { return product.id ? 'Costo promedio' : 'Costo de compra'; }
  /** Pone en el precio elegido el costo más la ganancia indicada, redondeado hacia arriba de 50 en 50. */
  applyProfitTo(target: 'retailPrice' | 'wholesalePrice', product: Product): void {
    const price = suggestedPrice(this.referenceCost(product), this.profitPercent());
    if (price === null) { this.priceHint.set('Indica el costo del producto y el % de ganancia para calcular el precio.'); return; }
    this.priceHint.set('');
    if (target === 'retailPrice') this.productForm.controls.retailPrice.setValue(price); else this.productForm.controls.wholesalePrice.setValue(String(price));
    this.productForm.controls[target].markAsDirty();
  }
  /** Cuánto se gana sobre el costo con el precio minorista que está escrito. */
  priceNote(product: Product): { text: string; low: boolean } | null {
    const percent = profitPercentOf(Number(this.productForm.controls.retailPrice.value), this.referenceCost(product));
    if (percent === null) return null;
    return percent < 0 ? { text: 'El precio minorista está por debajo del costo.', low: true } : { text: `Con el precio minorista ganas ${percent} % sobre el costo.`, low: false };
  }

  setHasTax(enabled: boolean): void {
    const control = this.productForm.controls.taxPercentage;
    this.hasTax.set(enabled);
    control.setValidators(enabled ? [Validators.required, Validators.min(0), Validators.max(100)] : [Validators.min(0), Validators.max(100)]);
    if (!enabled) control.setValue('');
    control.updateValueAndValidity();
  }

  async ngOnInit(): Promise<void> {
    if (!this.session.has('PRODUCTS.READ') && this.session.has('CATEGORIES.READ')) this.tab.set('categories');
    await Promise.all([this.reload(), this.loadOptions()]);
    this.restorePendingCreate();
  }

  async loadOptions(): Promise<void> {
    try {
      if (this.session.has('PRODUCTS.READ')) this.units.set(await this.api.units());
      this.applyDefaultUnit();
      if (this.session.has('SUPPLIERS.READ')) this.suppliers.set(await this.api.activeSuppliers());
      if (this.session.has('CATEGORIES.READ')) {
        const page = await this.api.categories({ page: 1, pageSize: 100 });
        this.categoryOptions.set(page.items.filter(x => x.isActive));
      }
    } catch (e) { this.error.set(errorText(e)); }
  }
  readonly categoryOptions = signal<Category[]>([]);

  private applyDefaultUnit(): void {
    const draft = this.selectedProduct();
    if (draft && !draft.id && !this.productForm.controls.measurementUnitId.value) {
      const unit = this.units().find(x => x.isActive && x.code.trim().toUpperCase() === 'UNIDAD');
      if (unit) this.productForm.controls.measurementUnitId.setValue(unit.id);
    }
  }

  async reload(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true); this.error.set('');
    try {
      if (this.tab() === 'products' && this.session.has('PRODUCTS.READ')) {
        const f = this.filters.getRawValue();
        const result = await this.api.products({
          search: f.productSearch.trim(), categoryId: f.categoryId || undefined,
          includeInactive: this.session.has('PRODUCTS.SENSITIVE_UPDATE') && f.includeInactiveProducts,
          page: this.productPage(), pageSize: this.productPageSize(),
        });
        if (id === this.requestId) this.products.set(result);
      } else if (this.session.has('CATEGORIES.READ')) {
        const f = this.filters.getRawValue();
        const result = await this.api.categories({
          search: f.categorySearch.trim(), includeInactive: f.includeInactiveCategories,
          page: this.categoryPage(), pageSize: this.categoryPageSize(),
        });
        if (id === this.requestId) this.categories.set(result);
      }
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  switchTab(tab: 'products' | 'categories'): void {
    this.tab.set(tab); this.cancel(); void this.reload();
  }
  search(): void {
    if (this.tab() === 'products') this.productPage.set(1); else this.categoryPage.set(1);
    void this.reload();
  }
  pageChanged(event: PageEvent): void {
    if (this.tab() === 'products') { this.productPage.set(event.pageIndex + 1); this.productPageSize.set(event.pageSize); }
    else { this.categoryPage.set(event.pageIndex + 1); this.categoryPageSize.set(event.pageSize); }
    void this.reload();
  }

  editCategory(category: Category): void {
    this.selectedCategory.set(category); this.selectedProduct.set(null);
    this.categoryForm.setValue({ name: category.name, description: category.description ?? '', abbreviation: category.abbreviation ?? '' });
    this.setAbbreviationRequired(category.abbreviation !== null);
  }
  newCategory(): void {
    this.cancel(); this.selectedCategory.set({ id: '', name: '', description: null, isActive: true, version: 0, abbreviation: null });
    this.setAbbreviationRequired(true);
  }
  // New categories and categories that already have one must keep it; legacy
  // categories (null) may be saved without one but cannot receive new products.
  private setAbbreviationRequired(required: boolean): void {
    const control = this.categoryForm.controls.abbreviation;
    control.setValidators(required ? [Validators.required, Validators.pattern(/^[A-Za-z]{2,10}$/)] : [Validators.pattern(/^[A-Za-z]{2,10}$/)]);
    control.updateValueAndValidity();
  }
  /** Categories without abbreviation cannot be used for new products (server rule). */
  canCreateProductIn(category: Category): boolean { return !!category.abbreviation; }
  hasCategoriesWithoutAbbreviation(): boolean { return this.categoryOptions().some(x => !this.canCreateProductIn(x)); }
  editProduct(product: Product): void {
    this.selectedProduct.set(product); this.selectedCategory.set(null); this.generateBarcode.set(false);
    this.productForm.controls.internalCode.setValidators([Validators.required, Validators.maxLength(50)]);
    this.productForm.controls.internalCode.updateValueAndValidity();
    this.productForm.setValue({
      name: product.name, internalCode: product.internalCode, categoryId: product.categoryId,
      measurementUnitId: product.measurementUnitId, barcode: product.barcode ?? '',
      supplierId: product.supplierId ?? '',
      barcodeFormat: product.barcodeFormat ?? '', description: product.description ?? '',
      retailPrice: product.retailPrice, wholesalePrice: product.wholesalePrice?.toString() ?? '',
      taxPercentage: product.taxPercentage?.toString() ?? '', minimumStock: product.minimumStock,
      initialQuantity: 0, initialUnitCost: '',
    });
    this.setHasTax(product.taxPercentage != null);
  }
  newProduct(): void {
    if (this.saving() || this.checkingCreate()) return;
    if (this.restorePendingCreate()) return;
    this.cancel(); this.generateBarcode.set(true);
    this.selectedProduct.set({ id: '', version: 0 } as Product);
    this.productForm.reset({
      name: '', internalCode: '', categoryId: '', measurementUnitId: '', supplierId: '',
      barcode: '', barcodeFormat: '', description: '', retailPrice: 0,
      wholesalePrice: '', taxPercentage: '', minimumStock: 0, initialQuantity: 0, initialUnitCost: '',
    });
    this.setHasTax(false);
    this.applyDefaultUnit();
  }
  cancel(): void {
    const pending = this.createAttempt?.state === 'pending';
    if (!pending) this.createAttempt = null;
    this.selectedCategory.set(null); this.selectedProduct.set(null);
    this.costHistory.set(null);
    this.productForm.controls.internalCode.clearValidators();
    this.productForm.controls.internalCode.updateValueAndValidity();
    this.categoryForm.reset(); this.setAbbreviationRequired(false); this.productForm.reset(); this.notice.set('');
    this.createOutcomeUncertain.set(pending); this.createErrorKind.set(pending ? 'uncertain' : null); this.reconciliationCandidates.set([]); this.reconciliationQueryFailed.set(false);
  }

  private readCreateAttempt(): ProductCreateAttempt | null {
    if (this.createAttempt) return this.createAttempt;
    try {
      const saved = JSON.parse(sessionStorage.getItem(this.createAttemptStorageKey) ?? 'null');
      if (saved && typeof saved.body === 'string' && typeof saved.key === 'string' && typeof saved.actorId === 'string') {
        // Old drafts did not record the outcome; do not assume that their POST failed.
        this.createAttempt = { ...saved, state: saved.state === 'rejected' ? 'rejected' : 'pending' };
      }
    } catch { /* Invalid old draft. */ }
    return this.createAttempt;
  }

  private storeCreateAttempt(): void {
    sessionStorage.setItem(this.createAttemptStorageKey, JSON.stringify(this.createAttempt));
  }

  private restorePendingCreate(): boolean {
    const attempt = this.readCreateAttempt();
    if (attempt?.state !== 'pending') return false;
    this.createOutcomeUncertain.set(true); this.createErrorKind.set('uncertain');
    this.error.set('Hay un alta pendiente de confirmar. Recupera el alta original antes de iniciar otra creación.');
    if (attempt.actorId !== this.session.user()?.id) {
      this.error.set('El alta pendiente pertenece a otro operador. Debe recuperarla con su sesión original.');
      return true;
    }
    const original = JSON.parse(attempt.body) as CreateProductInput & { generateBarcode: boolean };
    this.selectedProduct.set({ id: '', version: 0 } as Product); this.selectedCategory.set(null);
    this.productForm.controls.internalCode.clearValidators();
    this.productForm.controls.internalCode.updateValueAndValidity();
    this.productForm.reset({ ...original, internalCode: '', supplierId: original.supplierId ?? '',
      barcode: original.barcode ?? '', barcodeFormat: original.barcodeFormat ?? '',
      description: original.description ?? '', wholesalePrice: original.wholesalePrice?.toString() ?? '',
      taxPercentage: original.taxPercentage?.toString() ?? '', initialUnitCost: original.initialUnitCost?.toString() ?? '',
    });
    this.generateBarcode.set(original.generateBarcode);
    this.setHasTax(original.taxPercentage != null);
    return true;
  }

  async saveCategory(): Promise<void> {
    if (this.categoryForm.invalid || this.saving()) { this.categoryForm.markAllAsTouched(); return; }
    const selected = this.selectedCategory();
    if (!selected) return;
    this.saving.set(true); this.error.set('');
    try {
      const { name, description, abbreviation } = this.categoryForm.getRawValue();
      const code = abbreviation.trim().toUpperCase();
      if (selected.id) await this.api.updateCategory(selected, name.trim(), description.trim() || null, code || null);
      else await this.api.createCategory(name.trim(), description.trim() || null, code);
      this.cancel(); this.notice.set('Categoría guardada.');
      await Promise.all([this.reload(), this.loadOptions()]);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  saveProduct(): Promise<void> { return this.submitProduct(false); }
  recoverCreate(): Promise<void> { return this.submitProduct(true); }

  private async submitProduct(recoverOriginal: boolean): Promise<void> {
    if (this.saving() || this.checkingCreate()) return;
    const pending = this.readCreateAttempt()?.state === 'pending';
    if (recoverOriginal) {
      if (!pending || !this.restorePendingCreate() || this.createAttempt?.actorId !== this.session.user()?.id) return;
    } else if (pending && !this.selectedProduct()?.id) {
      this.createOutcomeUncertain.set(true); this.createErrorKind.set('uncertain');
      this.error.set('Hay un alta pendiente de confirmar. Recupera el alta original; los cambios no se enviarán con otra clave.');
      return;
    }
    if (this.productForm.invalid || this.saving() || this.checkingCreate()) { this.productForm.markAllAsTouched(); return; }
    const selected = this.selectedProduct();
    if (!selected) return;
    const f = this.productForm.getRawValue();
    if (!selected.id) {
      if (Number(f.initialQuantity) > 0) {
        this.checkingCreate.set(true);
        try { await this.session.refreshUser(); }
        catch (e) { this.error.set(errorText(e)); return; }
        finally { this.checkingCreate.set(false); }
      }
      if (!this.session.has('PRODUCTS.CREATE')) { this.error.set('No tienes permiso para crear productos.'); return; }
      const quantity = Number(f.initialQuantity);
      if (quantity > 0 && !this.canCreateWithInitialStock()) {
        this.error.set('Solo Administrador o Supervisor con permiso de crear productos puede registrar existencias iniciales.'); return;
      }
      if (quantity > 0 && f.initialUnitCost === '') {
        this.productForm.controls.initialUnitCost.setErrors({ required: true });
        this.productForm.controls.initialUnitCost.markAsTouched(); return;
      }
      if (quantity === 0 && f.initialUnitCost !== '') {
        this.error.set('El costo inicial requiere existencias positivas.'); return;
      }
    }
    this.saving.set(true); this.error.set('');
    try {
      const input: ProductInput = {
        name: f.name.trim(), categoryId: f.categoryId,
        measurementUnitId: f.measurementUnitId, supplierId: this.session.has('SUPPLIERS.READ') ? f.supplierId || null : selected.supplierId ?? null,
        barcode: this.generateBarcode() && !selected.id ? null : f.barcode.trim() || null,
        barcodeFormat: selected.id && f.barcode.trim() === (selected.barcode ?? '') ? selected.barcodeFormat : null,
        description: f.description.trim() || null, retailPrice: Number(f.retailPrice),
        wholesalePrice: f.wholesalePrice === '' ? null : Number(f.wholesalePrice),
        taxPercentage: null, // El IVA se quitó del sistema (18 de noviembre de 2026).
        minimumStock: Number(f.minimumStock),
      };
      let saved: Product;
      if (selected.id) saved = await this.api.updateProduct(selected, { ...input, internalCode: f.internalCode.trim() });
      else {
        let createInput: CreateProductInput = { ...input, initialQuantity: Number(f.initialQuantity),
          initialUnitCost: f.initialUnitCost === '' ? null : Number(f.initialUnitCost) };
        let generateBarcode = this.generateBarcode();
        if (recoverOriginal && this.createAttempt) {
          const original = JSON.parse(this.createAttempt.body) as CreateProductInput & { generateBarcode: boolean };
          ({ generateBarcode, ...createInput } = original);
        } else {
          const body = JSON.stringify({ ...createInput, generateBarcode });
          const actorId = this.session.user()?.id ?? '';
          if (this.createAttempt?.body !== body || this.createAttempt.actorId !== actorId) {
            this.createAttempt = { actorId, body, key: crypto.randomUUID(), state: 'pending' };
          }
        }
        this.createAttempt!.state = 'pending'; this.storeCreateAttempt();
        this.createOutcomeUncertain.set(recoverOriginal); this.createErrorKind.set(null); this.reconciliationCandidates.set([]);
        saved = await this.api.createProduct(createInput, generateBarcode, this.createAttempt!.key);
        sessionStorage.removeItem(this.createAttemptStorageKey);
        this.createAttempt = null;
      }
      this.cancel();
      if (!selected.id) {
        this.tab.set('products'); this.productPage.set(1);
        this.filters.patchValue({ productSearch: saved.internalCode, categoryId: '' });
      }
      this.notice.set(`Producto guardado. N.º de producto: ${saved.internalCode}.`); await this.reload();
    } catch (e) {
      this.error.set(errorText(e));
      if (!selected.id) this.handleCreateFailure(e, recoverOriginal);
    }
    finally { this.saving.set(false); }
  }

  private handleCreateFailure(error: unknown, recovering: boolean): void {
    const uncertain = recovering || !(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500;
    if (this.createAttempt) {
      this.createAttempt.state = uncertain ? 'pending' : 'rejected';
      this.storeCreateAttempt();
    }
    this.createOutcomeUncertain.set(uncertain);
    if (!(error instanceof HttpErrorResponse)) { this.createErrorKind.set('uncertain'); this.createOutcomeUncertain.set(true); void this.reconcileCreate(); return; }
    if (error.status === 400) { this.createErrorKind.set('validation'); return; }
    if (error.status === 409) {
      const message = errorText(error).toLocaleLowerCase();
      this.createErrorKind.set(message.includes('idempotencia') || message.includes('clave') ? 'idempotency' :
        message.includes('producto') || message.includes('código de barras') || message.includes('código interno') ? 'unique' : 'conflict');
      return;
    }
    if (error.status === 0 || error.status >= 500) {
      this.createErrorKind.set('uncertain'); this.createOutcomeUncertain.set(true); void this.reconcileCreate(); return;
    }
    this.createErrorKind.set('conflict');
  }

  async reconcileCreate(): Promise<void> {
    if (this.reconcilingCreate() || !this.createOutcomeUncertain()) return;
    let attempted: { barcode?: string | null; generateBarcode?: boolean; name: string; categoryId: string; measurementUnitId: string };
    try { attempted = JSON.parse(this.createAttempt?.body ?? '{}'); } catch { return; }
    const barcode = attempted.generateBarcode ? '' : attempted.barcode?.trim() ?? '';
    const search = barcode || attempted.name.trim();
    if (!search) return;
    this.reconcilingCreate.set(true);
    this.reconciliationQueryFailed.set(false);
    try {
      const matches: Product[] = [];
      let page = 1;
      let totalPages = 1;
      do {
        const result = await this.api.products({ search, categoryId: attempted.categoryId || undefined,
          includeInactive: false, page, pageSize: 200 });
        matches.push(...result.items);
        totalPages = result.totalPages;
        page++;
      } while (page <= totalPages);
      const candidates = matches.filter(product => barcode
        ? product.barcode?.trim().toLocaleLowerCase() === barcode.toLocaleLowerCase()
        : product.name.trim().toLocaleLowerCase() === attempted.name.trim().toLocaleLowerCase() &&
          product.categoryId === attempted.categoryId && product.measurementUnitId === attempted.measurementUnitId);
      this.reconciliationCandidates.set(candidates);
    } catch (error) {
      this.reconciliationQueryFailed.set(true);
      this.error.set(`${errorText(error)} La consulta no confirma si el alta se guardó.`);
    } finally { this.reconcilingCreate.set(false); }
  }

  canCreateWithInitialStock(): boolean {
    return this.session.has('PRODUCTS.CREATE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR');
  }

  async toggleCategory(category: Category): Promise<void> {
    this.saving.set(true); this.error.set('');
    try { await this.api.setCategoryActive(category); await Promise.all([this.reload(), this.loadOptions()]); }
    catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); }
  }
  async toggleProduct(product: Product): Promise<void> {
    this.saving.set(true); this.error.set('');
    try { await this.api.setProductActive(product); await this.reload(); }
    catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); }
  }
  /** Only an Administrator with the effective permission sees the button; the server enforces it too. */
  canDeleteProduct(): boolean { return this.session.hasRole('ADMINISTRADOR') && this.session.has('PRODUCTS.SENSITIVE_UPDATE'); }
  readonly deleting = signal<Product | null>(null);
  askDelete(product: Product): void { this.error.set(''); this.notice.set(''); this.deleting.set(product); }
  async confirmDelete(): Promise<void> {
    const product = this.deleting(); if (!product || this.saving()) return;
    this.saving.set(true); this.error.set('');
    try {
      await this.api.deleteProduct(product);
      this.deleting.set(null); this.notice.set(`Producto ${product.internalCode} borrado.`); await this.reload();
    } catch (e) {
      this.deleting.set(null);
      if (e instanceof HttpErrorResponse && e.status === 409 && e.error?.code === 'concurrency.conflict') { await this.reload(); this.error.set('El producto cambió mientras lo revisabas. Actualiza la lista e inténtalo de nuevo.'); }
      else if (e instanceof HttpErrorResponse && e.status === 409) this.error.set(`${product.internalCode} tiene historial (ventas, compras, apartados, pedidos o movimientos) y no se puede borrar. Puedes inactivarlo.`);
      else if (e instanceof HttpErrorResponse && e.status === 404) { await this.reload(); this.error.set('El producto ya no existe.'); }
      else this.error.set(errorText(e));
    } finally { this.saving.set(false); }
  }
  async showCosts(product: Product, page = 1): Promise<void> {
    this.error.set(''); this.costProductName.set(product.name); this.costProduct.set(product);
    try { this.costHistory.set(await this.api.costHistory(product.id, page)); }
    catch (e) { this.error.set(errorText(e)); }
  }
  showCostsByPage(event: PageEvent): void {
    const product = this.costProduct();
    if (product) void this.showCosts(product, event.pageIndex + 1);
  }
}
