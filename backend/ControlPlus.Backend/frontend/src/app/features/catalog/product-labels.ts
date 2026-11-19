import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { errorText } from '../../core/api';
import { code128Svg } from '../../core/code128';
import { Icon } from '../../layout/icon';
import { ProductLabelJobInput, ProductLabelProduct, ProductLabelProfile, ProductLabelsApi } from './product-labels-api';
import { toastSignal } from '../../core/toast';

interface SelectedProduct {
  product: ProductLabelProduct;
  quantity: number;
}

interface PrintAttempt {
  body: string;
  key: string;
}

@Component({
  selector: 'cp-product-labels',
  imports: [CurrencyPipe, FormsModule, Icon, RouterLink],
  templateUrl: './product-labels.html',
  styleUrl: './product-labels.scss',
})
export class ProductLabels implements OnInit {
  private readonly api = inject(ProductLabelsApi);
  private readonly sanitizer = inject(DomSanitizer);
  @ViewChild('productSearch') private searchInput?: ElementRef<HTMLInputElement>;

  readonly profiles = signal<ProductLabelProfile[]>([]);
  readonly results = signal<ProductLabelProduct[]>([]);
  readonly selected = signal<SelectedProduct[]>([]);
  readonly selectedProfileId = signal('');
  readonly loading = signal(false);
  readonly printing = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly totalLabels = computed(() => this.selected().reduce((total, item) => total + item.quantity, 0));
  readonly preview = computed(() => this.selected()[0]?.product ?? null);
  readonly selectedProfile = computed(() => this.profiles().find(profile => profile.id === this.selectedProfileId()) ?? null);
  readonly previewBarcode = computed<SafeHtml | null>(() => {
    const barcode = this.preview()?.barcode;
    if (!barcode) return null;
    try { return this.sanitizer.bypassSecurityTrustHtml(code128Svg(barcode, 1.5, 54)); }
    catch { return null; }
  });

  search = '';
  private attempt: PrintAttempt | null = null;

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    try {
      const profiles = await this.api.profiles();
      this.profiles.set(profiles);
      const preferred = profiles.find(profile => profile.isDefault) ?? profiles[0];
      this.selectedProfileId.set(preferred?.id ?? '');
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }

  async find(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true); this.error.set(''); this.notice.set('');
    try {
      const term = this.search.trim();
      const page = await this.api.products(term, 1, 20);
      const exact = page.items.filter(product =>
        product.internalCode.toLocaleLowerCase() === term.toLocaleLowerCase() || product.barcode === term);
      if (term && exact.length === 1) this.add(exact[0]);
      else this.results.set(page.items);
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }

  add(product: ProductLabelProduct): void {
    if (!product.barcode) {
      this.error.set(`${product.name} no tiene código de barras y no puede imprimirse.`);
      return;
    }
    this.selected.update(items => {
      const current = items.find(item => item.product.id === product.id);
      return current
        ? items.map(item => item.product.id === product.id ? { ...item, quantity: Math.min(500, item.quantity + 1) } : item)
        : [...items, { product, quantity: 1 }];
    });
    this.search = ''; this.results.set([]); this.error.set(''); this.notice.set(''); this.attempt = null;
    this.focusSearch();
  }

  remove(productId: string): void {
    this.selected.update(items => items.filter(item => item.product.id !== productId));
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  changeQuantity(productId: string, value: number | string): void {
    const parsed = Number(value);
    const quantity = Number.isInteger(parsed) ? Math.max(1, Math.min(500, parsed)) : 1;
    this.selected.update(items => items.map(item => item.product.id === productId ? { ...item, quantity } : item));
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  chooseProfile(profileId: string): void {
    this.selectedProfileId.set(profileId);
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  showProducts(): void { this.search = ''; void this.find().then(() => this.focusSearch()); }

  async print(): Promise<void> {
    if (this.printing()) return;
    const total = this.totalLabels();
    if (!this.selectedProfileId()) { this.error.set('Configura y selecciona un perfil de etiquetas o híbrido.'); return; }
    if (total < 1) { this.error.set('Agrega al menos un producto.'); return; }
    if (total > 500) { this.error.set('Un lote admite hasta 500 etiquetas.'); return; }
    const input: ProductLabelJobInput = {
      profileId: this.selectedProfileId(),
      lines: this.selected().map(item => ({ productId: item.product.id, quantity: item.quantity })),
    };
    const body = JSON.stringify(input);
    if (!this.attempt || this.attempt.body !== body) this.attempt = { body, key: crypto.randomUUID() };

    this.printing.set(true); this.error.set(''); this.notice.set('');
    try {
      const job = await this.api.queue(input, this.attempt.key);
      this.notice.set(`${job.labelCount} etiqueta${job.labelCount === 1 ? '' : 's'} enviada${job.labelCount === 1 ? '' : 's'} a ${job.profileName}. “Impreso” confirmará que la cola de Windows aceptó el trabajo.`);
      this.attempt = null;
    } catch (error) {
      this.error.set(errorText(error));
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) this.attempt = null;
    } finally { this.printing.set(false); }
  }

  private focusSearch(): void { queueMicrotask(() => this.searchInput?.nativeElement.focus()); }
}
