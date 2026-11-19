import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { CashFlowReport, ReportsApi, SalesOverview, saveFile } from './reports-api';
import { aggregateCashFlow, barHeights, defaultRange, methodLabel, originLabel, validateRange } from './reports-math';
import { toastSignal } from '../../core/toast';

export type ReportKind = 'sales' | 'cash';
const NOT_AVAILABLE = 'No disponible';
const MAX_LABELLED_BARS = 10;

@Component({
  selector: 'cp-reports',
  imports: [FormsModule, Icon],
  templateUrl: './reports.html',
  styleUrl: './reports.scss',
})
export class Reports implements OnInit {
  private readonly api = inject(ReportsApi);
  readonly session = inject(SessionService);
  readonly notAvailable = NOT_AVAILABLE;
  readonly sales = signal<SalesOverview | null>(null);
  readonly cash = signal<CashFlowReport | null>(null);
  /** Which report the data on screen belongs to; the selector may already show another one. */
  readonly shown = signal<ReportKind | null>(null);
  readonly loading = signal(false);
  readonly downloading = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  from = defaultRange().from;
  to = defaultRange().to;
  kind: ReportKind = 'sales';
  private requestId = 0;

  /** Sales figures include costs, so they need both permissions; cash flow needs only the financial one. */
  get canSales(): boolean { return this.session.has('REPORTS.FINANCIAL_READ') && this.session.has('PRODUCTS.COSTS_READ'); }
  get canCash(): boolean { return this.session.has('REPORTS.FINANCIAL_READ'); }
  get kinds(): { value: ReportKind; label: string }[] {
    return [...(this.canSales ? [{ value: 'sales' as const, label: 'Ventas' }] : []), ...(this.canCash ? [{ value: 'cash' as const, label: 'Flujo de Caja' }] : [])];
  }
  get canPdf(): boolean { return this.canSales && this.kind === 'sales'; }
  get rangeIssue(): string { return validateRange(this.from, this.to); }

  readonly salesBars = computed(() => {
    const report = this.sales(); if (!report) return [];
    const heights = barHeights(report.days.map(day => day.salesTotal));
    const labelled = report.days.length <= MAX_LABELLED_BARS;
    return report.days.map((day, index) => ({
      date: day.date, height: heights[index],
      label: labelled ? this.compact(day.salesTotal) : '',
      day: labelled ? `${day.date.slice(8, 10)}/${day.date.slice(5, 7)}` : String(Number(day.date.slice(8, 10))),
      title: `${day.date.split('-').reverse().join('/')}: ${this.money(day.salesTotal)}`,
    }));
  });
  /** A period without sales, changes or immediate payments shows an empty state instead of flat bars. */
  readonly salesEmpty = computed(() => {
    const report = this.sales();
    return !!report && report.sales.transactions === 0 && report.changes.transactions === 0 && report.days.every(day => day.salesTotal === 0 && day.changesNetTotal === 0);
  });
  readonly cashRows = computed(() => aggregateCashFlow(this.cash()?.days ?? []));
  readonly cashEmpty = computed(() => { const report = this.cash(); return !!report && report.days.every(day => !day.payments.length && !day.manualMovements.length && day.openingBalance === 0); });

  ngOnInit(): void {
    if (!this.kinds.some(option => option.value === this.kind)) this.kind = this.kinds[0]?.value ?? 'cash';
    void this.apply();
  }

  kindChanged(): void { this.notice.set(''); this.error.set(''); }

  async apply(): Promise<void> {
    const kind = this.kind;
    if (!this.kinds.some(option => option.value === kind)) { this.error.set('No tienes permiso para este reporte.'); return; }
    const issue = this.rangeIssue;
    if (issue) { this.error.set(issue); return; }
    const id = ++this.requestId;
    this.loading.set(true); this.error.set(''); this.notice.set('');
    const { from, to } = this;
    try {
      const data = kind === 'sales' ? await this.api.salesOverview(from, to) : await this.api.cashFlow(from, to);
      if (id !== this.requestId) return;
      if (kind === 'sales') this.sales.set(data as SalesOverview); else this.cash.set(data as CashFlowReport);
      this.shown.set(kind);
    } catch (e) { if (id === this.requestId) { this.error.set(errorText(e)); this.shown.set(null); } }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  async downloadPdf(): Promise<void> {
    if (!this.canPdf || this.downloading()) return;
    const issue = this.rangeIssue;
    if (issue) { this.error.set(issue); return; }
    this.downloading.set(true); this.error.set(''); this.notice.set('');
    try {
      const file = await this.api.salesPdf(this.from, this.to);
      saveFile(file);
      this.notice.set(`PDF generado: ${file.fileName}.`);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.downloading.set(false); }
  }

  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
  compact(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', notation: 'compact', maximumFractionDigits: 1 }).format(value); }
  /** Percentages come already rounded by the server; they are only formatted here. */
  percent(value: number | null): string { return value === null ? NOT_AVAILABLE : `${new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value)} %`; }
  optionalMoney(value: number | null): string { return value === null ? NOT_AVAILABLE : this.money(value); }
  /** Cost as a share of the net sales, only when both figures exist. */
  costShare(report: SalesOverview): string { return report.estimatedCost === null || report.netSalesTotal <= 0 ? '' : this.percent(Math.round((report.estimatedCost / report.netSalesTotal) * 10_000) / 100); }
  variationText(report: SalesOverview): string {
    const value = report.salesVariationPercentage;
    return value === null ? 'Sin período anterior con ventas para comparar' : `${value > 0 ? '+' : ''}${this.percent(value)} frente al período anterior`;
  }
  missingText(report: SalesOverview): string {
    const lines = report.missingCostLines + report.missingMovementLines;
    return lines > 0 ? `Faltan costos históricos en ${lines} ${lines === 1 ? 'línea' : 'líneas'}.` : 'Faltan costos históricos.';
  }
  origin(code: string): string { return originLabel(code); }
  method(code: string | null): string { return methodLabel(code); }
  dateText(iso: string): string { return iso.split('-').reverse().join('/'); }
}
