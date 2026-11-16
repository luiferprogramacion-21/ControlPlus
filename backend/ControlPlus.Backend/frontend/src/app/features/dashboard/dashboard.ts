import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { DashboardActivity, DashboardApi, DashboardSummary } from './dashboard-api';

export interface HourBar {
  label: string;
  /** Height as a percentage of the busiest hour; a non-empty hour is never invisible. */
  heightPercent: number;
  peak: boolean;
  description: string;
}

export interface AlertText {
  text: string;
  tone: 'success' | 'danger' | 'info' | 'clear';
  title: string;
}

export interface ComparisonText {
  text: string;
  tone: 'up' | 'down' | 'flat' | 'none';
}

@Component({
  selector: 'cp-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly api = inject(DashboardApi);
  /** Inventario solo abre con INVENTORY.READ; sin él no se ofrece el enlace. */
  readonly canOpenInventory = inject(SessionService).has('INVENTORY.READ');
  /** Inventario filtrado por el mismo criterio de stock bajo que cuenta el Dashboard. */
  readonly lowStockQuery = { filtro: 'stock-bajo' };
  readonly summary = signal<DashboardSummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

  readonly bars = computed<HourBar[]>(() => {
    const hours = this.summary()?.hourlySales ?? [];
    const max = Math.max(0, ...hours.map((hour) => hour.total));
    return hours.map((hour) => ({
      label: `${hour.hour}:00`,
      heightPercent: max > 0 && hour.total > 0 ? Math.max(4, Math.round((hour.total / max) * 100)) : 0,
      peak: max > 0 && hour.total === max,
      description: `${hour.hour}:00 · ${this.money(hour.total)} · ${this.count(hour.transactions, 'venta', 'ventas')}`,
    }));
  });

  ngOnInit(): void { void this.refresh(); }

  async refresh(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    try {
      this.summary.set(await this.api.summary());
    } catch (error) {
      this.summary.set(null);
      this.error.set(errorText(error));
    } finally {
      this.loading.set(false);
    }
  }

  money(amount: number): string {
    return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(amount);
  }

  date(iso: string): string {
    const [year, month, day] = iso.split('-').map(Number);
    return new Intl.DateTimeFormat('es-CO', { dateStyle: 'full', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, day)));
  }

  time(iso: string): string {
    return new Intl.DateTimeFormat('es-CO', { hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(iso));
  }

  averageTicket(data: DashboardSummary): string {
    return data.totalSales.transactions === 0 ? 'No disponible' : this.money(data.totalSales.total / data.totalSales.transactions);
  }

  count(amount: number, singular: string, plural: string): string {
    return `${amount} ${amount === 1 ? singular : plural}`;
  }

  /** An empty alert reads "Ninguno" instead of a zero count. */
  alertValue(amount: number, singular: string, plural: string): string {
    return amount === 0 ? 'Ninguno' : this.count(amount, singular, plural);
  }

  /** Today against yesterday up to the same time, never against the whole previous day. */
  comparison(data: DashboardSummary): ComparisonText {
    const percent = data.comparison.percentChange;
    if (percent === null) return { text: 'Sin ventas ayer a esta hora', tone: 'none' };
    if (percent === 0) return { text: 'Igual que ayer a esta hora', tone: 'flat' };
    return { text: `${percent > 0 ? '+' : '-'}${Math.abs(percent)} % frente a ayer a esta hora`, tone: percent > 0 ? 'up' : 'down' };
  }

  /** An open shift from a previous day also shows its date, so it is not mistaken for today's. */
  openedText(data: DashboardSummary): string {
    const cash = data.currentCash;
    if (!cash) return '';
    const opened = new Date(cash.openedAt);
    const sameDay = new Intl.DateTimeFormat('en-CA').format(opened) === data.operatingDate;
    const day = new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit' }).format(opened);
    return `Abierta desde ${sameDay ? '' : `${day} `}${this.time(cash.openedAt)}`;
  }

  /** Last external copy: a time from another day also shows its date. */
  externalBackup(data: DashboardSummary): AlertText {
    const backup = data.externalBackup;
    const at = (iso: string | null) => iso ? this.moment(iso, data.operatingDate) : '';
    const lastGood = backup.lastSuccessAt ? `Última copia correcta: ${at(backup.lastSuccessAt)}` : 'Aún no hay una copia correcta';
    switch (backup.state) {
      case 'CORRECTO': return { text: `Confirmado ${at(backup.lastSuccessAt)}`, tone: 'success', title: 'Copia externa cifrada enviada y verificada' };
      case 'FALLIDO': return { text: `Falló ${at(backup.lastAttemptAt)}`, tone: 'danger', title: lastGood };
      case 'ENVIANDO': return { text: 'Enviando…', tone: 'info', title: lastGood };
      case 'SIN_COPIAS': return { text: 'Sin copias todavía', tone: 'clear', title: 'El destino externo está configurado y aún no recibe copias' };
      default: return { text: 'Sin configurar', tone: 'clear', title: 'Configure la copia externa en Soporte y continuidad' };
    }
  }

  private moment(iso: string, operatingDate: string): string {
    const value = new Date(iso);
    const sameDay = new Intl.DateTimeFormat('en-CA').format(value) === operatingDate;
    const day = new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit' }).format(value);
    return `${sameDay ? '' : `${day} `}${this.time(iso)}`;
  }

  statusLabel(activity: DashboardActivity): string {
    if (activity.state === 'ANULADA') return 'Anulada';
    return activity.saleType === 'CREDITO' ? 'Crédito' : 'Confirmada';
  }

  statusClass(activity: DashboardActivity): string {
    if (activity.state === 'ANULADA') return 'badge--error';
    return activity.saleType === 'CREDITO' ? 'badge--warning' : 'badge--success';
  }
}
