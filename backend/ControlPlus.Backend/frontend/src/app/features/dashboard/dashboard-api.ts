import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DashboardAmount {
  transactions: number;
  total: number;
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
}

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  summary(): Promise<DashboardSummary> {
    return firstValueFrom(this.http.get<DashboardSummary>('/api/dashboard/summary'));
  }
}
