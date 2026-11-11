import { Component, inject, OnInit, signal } from '@angular/core';
import { errorText } from '../../core/api';
import { DashboardApi, DashboardSummary } from './dashboard-api';

@Component({
  selector: 'cp-dashboard',
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly api = inject(DashboardApi);
  readonly summary = signal<DashboardSummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

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

  averageTicket(data: DashboardSummary): string {
    return data.totalSales.transactions === 0 ? 'No disponible' : this.money(data.totalSales.total / data.totalSales.transactions);
  }
}
