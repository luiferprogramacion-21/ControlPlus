import { groupedNumber, NumberInput } from '../../shared/number-input';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { errorText, Page } from '../../core/api';
import { code128Svg } from '../../core/code128';
import { Icon } from '../../layout/icon';
import { ProductLabelJobInput, ProductLabelProduct, ProductLabelProfile, ProductLabelQueueItem, ProductLabelReceipt, ProductLabelsApi } from './product-labels-api';
import { toastSignal } from '../../core/toast';
import { EstablishmentService } from '../../core/establishment';

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
  imports: [NumberInput, DecimalPipe, CurrencyPipe, DatePipe, FormsModule, Icon, RouterLink],
  templateUrl: './product-labels.html',
  styleUrl: './product-labels.scss',
})
export class ProductLabels implements OnInit {
  private readonly api = inject(ProductLabelsApi);
  private readonly route = inject(ActivatedRoute, { optional: true });
  readonly establishment = inject(EstablishmentService);
  private readonly sanitizer = inject(DomSanitizer);
  @ViewChild('productSearch') private searchInput?: ElementRef<HTMLInputElement>;

  readonly profiles = signal<ProductLabelProfile[]>([]);
  readonly results = signal<ProductLabelProduct[]>([]);
  readonly selected = signal<SelectedProduct[]>([]);
  readonly selectedProfileId = signal('');
  readonly tab = signal<'prepare' | 'receipts' | 'jobs'>('prepare');
  readonly sourceReceipt = signal<ProductLabelReceipt | null>(null);
  readonly receipts = signal<Page<ProductLabelReceipt> | null>(null);
  readonly loadingReceipts = signal(false);
  readonly jobs = signal<Page<ProductLabelQueueItem> | null>(null);
  readonly loadingJobs = signal(false);
  readonly retryingJob = signal<string | null>(null);
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
    try { return this.sanitizer.bypassSecurityTrustHtml(code128Svg(barcode, 1.5, 40)); }
    catch { return null; }
  });

  search = '';
  private attempt: PrintAttempt | null = null;

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    try {
      await this.establishment.load();
      const profiles = await this.api.profiles();
      this.profiles.set(profiles);
      const preferred = profiles.find(profile => profile.isDefault) ?? profiles[0];
      this.selectedProfileId.set(preferred?.id ?? '');
      const purchaseId = this.route?.snapshot.queryParamMap.get('compra');
      if (purchaseId) this.prepareReceipt(await this.api.receipt(purchaseId));
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }

  async find(): Promise<void> {
    if (this.loading() || this.sourceReceipt()) return;
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
    if (this.printing() || this.sourceReceipt()) return;
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
    if (this.printing()) return;
    this.selected.update(items => items.filter(item => item.product.id !== productId));
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  changeQuantity(productId: string, value: number | string): void {
    if (this.printing()) return;
    const parsed = Number(value);
    const quantity = Number.isInteger(parsed) ? Math.max(1, Math.min(this.maximumFor(productId), parsed)) : 1;
    this.selected.update(items => items.map(item => item.product.id === productId ? { ...item, quantity } : item));
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  chooseProfile(profileId: string): void {
    if (this.printing()) return;
    this.selectedProfileId.set(profileId);
    this.error.set(''); this.notice.set(''); this.attempt = null;
  }

  showProducts(): void { this.search = ''; void this.find().then(() => this.focusSearch()); }

  maximumFor(productId: string): number {
    return Math.min(500, this.sourceReceipt()?.lines.find(line => line.product.id === productId)?.pendingQuantity ?? 500);
  }

  async showReceipts(page = 1): Promise<void> {
    if (this.loadingReceipts()) return;
    this.tab.set('receipts'); this.loadingReceipts.set(true);
    try { this.receipts.set(await this.api.receipts(page)); }
    catch (error) { this.error.set(errorText(error)); }
    finally { this.loadingReceipts.set(false); }
  }

  prepareReceipt(receipt: ProductLabelReceipt): void {
    if (this.printing()) return;
    let remaining = 500;
    const items: SelectedProduct[] = [];
    for (const line of receipt.lines) {
      if (!line.product.barcode || line.pendingQuantity <= 0 || remaining === 0 || items.length === 100) continue;
      const quantity = Math.min(line.pendingQuantity, remaining);
      items.push({ product: line.product, quantity }); remaining -= quantity;
    }
    this.sourceReceipt.set(receipt); this.selected.set(items); this.results.set([]); this.search = '';
    this.tab.set('prepare'); this.attempt = null;
    if (items.reduce((sum, item) => sum + item.quantity, 0) < receipt.pendingLabelCount) {
      this.notice.set('Se prepararon las etiquetas con código de barras que caben en este lote (hasta 100 productos y 500 etiquetas). El resto seguirá pendiente.');
    }
  }

  clearReceipt(): void {
    if (this.printing()) return;
    this.sourceReceipt.set(null); this.selected.set([]); this.attempt = null; this.error.set(''); this.notice.set('');
  }

  async showJobs(page = 1): Promise<void> {
    if (this.loadingJobs()) return;
    this.tab.set('jobs'); this.loadingJobs.set(true);
    try { this.jobs.set(await this.api.jobs(page)); }
    catch (error) { this.error.set(errorText(error)); }
    finally { this.loadingJobs.set(false); }
  }

  jobStatus(status: string): string {
    return ({ PENDIENTE: 'Pendiente', IMPRIMIENDO: 'Enviando a impresora', IMPRESO: 'Impreso', ERROR: 'Error', CANCELADO: 'Cancelado' } as Record<string, string>)[status] ?? status;
  }

  async retryJob(job: ProductLabelQueueItem): Promise<void> {
    if (job.status !== 'ERROR' || this.retryingJob()) return;
    this.retryingJob.set(job.jobId);
    try {
      await this.api.retry(job.jobId);
      this.notice.set('El mismo lote se envía otra vez a la impresora.');
      await this.showJobs(this.jobs()?.page ?? 1);
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.retryingJob.set(null); }
  }

  async print(): Promise<void> {
    if (this.printing()) return;
    const total = this.totalLabels();
    if (!this.selectedProfileId()) { this.error.set('Configura y selecciona un perfil de etiquetas o híbrido.'); return; }
    if (total < 1) { this.error.set('Agrega al menos un producto.'); return; }
    if (total > 500) { this.error.set('Un lote admite hasta 500 etiquetas.'); return; }
    const input: ProductLabelJobInput = {
      profileId: this.selectedProfileId(),
      lines: this.selected().map(item => ({ productId: item.product.id, quantity: item.quantity })),
      ...(this.sourceReceipt() ? { sourcePurchaseId: this.sourceReceipt()!.purchaseId } : {}),
    };
    const body = JSON.stringify(input);
    if (!this.attempt || this.attempt.body !== body) this.attempt = { body, key: crypto.randomUUID() };

    this.printing.set(true); this.error.set(''); this.notice.set('');
    try {
      const job = await this.api.queue(input, this.attempt.key);
      this.notice.set(`${groupedNumber(job.labelCount)} etiqueta${job.labelCount === 1 ? '' : 's'} enviada${job.labelCount === 1 ? '' : 's'} a ${job.profileName}. “Impreso” indicará que la impresora lo recibió.`);
      if (this.sourceReceipt()) { this.sourceReceipt.set(null); this.selected.set([]); this.receipts.set(null); }
      this.attempt = null;
    } catch (error) {
      this.error.set(errorText(error));
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) this.attempt = null;
    } finally { this.printing.set(false); }
  }

  private focusSearch(): void { queueMicrotask(() => this.searchInput?.nativeElement.focus()); }
}
