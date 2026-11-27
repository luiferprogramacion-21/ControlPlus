import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { errorText } from '../../core/api';
import { RestorableCopy, RestoreOverview, RestoreTest, SupportApi } from './support-api';
import { toastSignal } from '../../core/toast';
import { groupedNumber, NumberInput } from '../../shared/number-input';
import { FormsModule } from '@angular/forms';
import { WindowSheet } from '../../shared/window-sheet';

/** Restauración y continuidad (maqueta 22c). Only the restore test is available; the real restore is a separate procedure. */
@Component({
  selector: 'cp-restore',
  imports: [RouterLink, FormsModule, NumberInput, WindowSheet],
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

  /** Cambiar metas (decisión del usuario del 27/11/2026: 30 minutos de pérdida, 30 de recuperación y prueba cada mes). */
  readonly editingGoals = signal(false);
  readonly savingGoals = signal(false);
  goals = { rpoMinutes: 30, rtoMinutes: 30, restoreTestDays: 30 };
  startGoals(): void {
    const data = this.overview(); if (!data) return;
    this.goals = { rpoMinutes: data.rpoMinutes, rtoMinutes: data.rtoMinutes, restoreTestDays: data.restoreTestDays };
    this.error.set(''); this.editingGoals.set(true);
  }
  async saveGoals(): Promise<void> {
    const data = this.overview(); if (!data || this.savingGoals()) return;
    const rpo = Number(this.goals.rpoMinutes), rto = Number(this.goals.rtoMinutes), days = Number(this.goals.restoreTestDays);
    if (!Number.isInteger(rpo) || rpo < 5 || rpo > 1440) { this.error.set('La pérdida máxima va de 5 a 1.440 minutos (un día).'); return; }
    if (!Number.isInteger(rto) || rto < 5 || rto > 1440) { this.error.set('El tiempo para recuperar va de 5 a 1.440 minutos (un día).'); return; }
    if (!Number.isInteger(days) || days < 1 || days > 365) { this.error.set('La prueba se hace cada 1 a 365 días.'); return; }
    this.savingGoals.set(true); this.error.set('');
    try {
      this.overview.set(await this.api.updateRestoreGoals({ rpoMinutes: rpo, rtoMinutes: rto, restoreTestDays: days, expectedVersion: data.goalsVersion ?? 0 }));
      this.editingGoals.set(false);
      this.notice.set('Metas guardadas.');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.savingGoals.set(false); }
  }
  readonly notice = toastSignal('success');

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
