import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { errorText } from '../../core/api';
import { RestorableCopy, RestoreOverview, RestoreTest, SupportApi } from './support-api';
import { toastSignal } from '../../core/toast';
import { groupedNumber } from '../../shared/number-input';

/** Restauración y continuidad (maqueta 22c). Only the restore test is available; the real restore is a separate procedure. */
@Component({
  selector: 'cp-restore',
  imports: [RouterLink],
  templateUrl: './restore.html',
  styleUrl: './support.scss',
})
export class Restore implements OnInit {
  private readonly api = inject(SupportApi);
  readonly number = groupedNumber;
  readonly overview = signal<RestoreOverview | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly loading = signal(false);
  readonly testing = signal(false);
  readonly error = toastSignal('error');
  readonly result = signal<RestoreTest | null>(null);
  readonly selected = computed(() => this.overview()?.copies.find(x => x.copyId === this.selectedId()) ?? null);

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const overview = await this.api.restoreOverview();
      this.overview.set(overview);
      if (!overview.copies.some(x => x.copyId === this.selectedId()))
        this.selectedId.set(overview.copies.find(x => x.available)?.copyId ?? null);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  select(copy: RestorableCopy): void { if (copy.available) { this.selectedId.set(copy.copyId); this.result.set(null); } }

  async test(): Promise<void> {
    const copy = this.selected(); if (!copy || this.testing()) return;
    this.testing.set(true); this.error.set(''); this.result.set(null);
    try { this.result.set(await this.api.restoreTest(copy.copyId)); await this.load(); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.testing.set(false); }
  }

  destination(type: string): string { return type === 'EXTERNO_CIFRADO' ? 'Externo cifrado (nube)' : 'Disco o USB de la tienda'; }

  dateTime(iso: string | null | undefined): string {
    if (!iso) return '—';
    const date = new Date(iso);
    const day = new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit' }).format(date);
    const time = new Intl.DateTimeFormat('es-CO', { hour: '2-digit', minute: '2-digit', hour12: false }).format(date);
    return `${day} · ${time}`;
  }

  date(value: string | null | undefined): string {
    if (!value) return '—';
    const [year, month, day] = value.slice(0, 10).split('-').map(Number);
    return new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, day)));
  }

  size(bytes: number | null): string {
    if (bytes === null) return '—';
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes; let unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return `${new Intl.NumberFormat('es-CO', { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`;
  }

  rto(minutes: number): string { return minutes % 60 === 0 ? `${groupedNumber(minutes / 60)} horas` : `${groupedNumber(minutes)} min`; }

  /** True when the next test date has arrived (Phase 4: at most every N days). */
  due(overview: RestoreOverview): boolean {
    return !overview.nextTestDue || overview.nextTestDue <= new Date().toISOString().slice(0, 10);
  }
}
