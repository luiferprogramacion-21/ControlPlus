import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DashboardAmount {
  transactions: number;
  total: number;
}

/** Today's sales against yesterday up to the same time; `percentChange` is null when yesterday had no sales. */
export interface DashboardComparison {
  previousDate: string;
  previousTotal: number;
  percentChange: number | null;
}

/** Expected cash of the open shift, the same figure the cash count shows. */
export interface DashboardCash {
  openedAt: string;
  expectedCash: number;
}

export interface DashboardAlerts {
  lowStockProducts: number;
  /** Pruebas del usuario del 26/11/2026: los agotados se cuentan aparte del stock bajo. */
  outOfStockProducts?: number;
  overdueCreditCustomers: number;
  upcomingLayaways: number;
  upcomingLayawayDays: number;
}

/** Last delivery to the encrypted external backup copy (no paths or errors). */
export interface DashboardExternalBackup {
  state: 'SIN_CONFIGURAR' | 'SIN_COPIAS' | 'CORRECTO' | 'FALLIDO' | 'ENVIANDO';
  lastAttemptAt: string | null;
  lastSuccessAt: string | null;
}

/** Confirmed sales of one local hour of the establishment. */
export interface DashboardHour {
  hour: number;
  transactions: number;
  total: number;
}

export interface DashboardActivity {
  saleId: string;
  number: string;
  occurredAt: string;
  customerName: string | null;
  total: number;
  saleType: 'CONTADO' | 'CREDITO';
  state: 'CONFIRMADA' | 'ANULADA';
}

/** Figures belong to the establishment's current operating date, chosen by the server. */
export interface DashboardSummary {
  operatingDate: string;
  cashSales: DashboardAmount;
  creditSales: DashboardAmount;
  totalSales: DashboardAmount;
  openingBalance: number;
  paymentTotal: number;
  manualTotal: number;
  netCashFlow: number;
  comparison: DashboardComparison;
  /** Null when there is no open shift. */
  currentCash: DashboardCash | null;
  alerts: DashboardAlerts;
  hourlySales: DashboardHour[];
  recentActivity: DashboardActivity[];
  externalBackup: DashboardExternalBackup;
}

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  summary(): Promise<DashboardSummary> {
    return firstValueFrom(this.http.get<DashboardSummary>('/api/dashboard/summary'));
  }
}
