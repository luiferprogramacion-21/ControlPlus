import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SalesOverview {
  from: string; to: string; currency: string;
  sales: { transactions: number; cashTotal: number; creditTotal: number; total: number };
  changes: { transactions: number; netTotal: number };
  netSalesTotal: number;
  previousSalesTotal: number;
  salesVariationPercentage: number | null;
  averageTicket: number | null;
  estimatedCost: number | null;
  estimatedMargin: number | null;
  estimatedMarginPercentage: number | null;
  costCompleteness: 'COMPLETE' | 'INCOMPLETE';
  missingCostLines: number;
  missingMovementLines: number;
  days: { date: string; salesTotal: number; changesNetTotal: number; netSalesTotal: number }[];
  paymentMethodsTotal: number;
  paymentMethods: { code: string; name: string; total: number; percentage: number }[];
}

export interface CashFlowEntry { date: string; origin: string; method: string | null; amount: number; }
export interface CashFlowDay { date: string; openingBalance: number; payments: CashFlowEntry[]; manualMovements: CashFlowEntry[]; paymentTotal: number; manualTotal: number; }
export interface CashFlowReport { from: string; to: string; days: CashFlowDay[]; openingBalance: number; paymentTotal: number; manualTotal: number; }

export interface PdfFile { blob: Blob; fileName: string; }

/** Same name the server proposes: reporte-ventas-YYYY-MM-DD_YYYY-MM-DD.pdf */
export const salesPdfName = (from: string, to: string): string => `reporte-ventas-${from}_${to}.pdf`;

/** A failed blob download carries its problem+json body as a Blob; this turns it back into a readable error. */
async function readableError(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) return error;
  let body: unknown = null;
  try { body = JSON.parse(await error.error.text()); } catch { /* not JSON: keep the status only */ }
  return new HttpErrorResponse({ error: body, status: error.status, statusText: error.statusText, url: error.url ?? undefined });
}

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);
  salesOverview(from: string, to: string): Promise<SalesOverview> { return firstValueFrom(this.http.get<SalesOverview>('/api/reports/sales/overview', { params: { from, to } })); }
  cashFlow(from: string, to: string): Promise<CashFlowReport> { return firstValueFrom(this.http.get<CashFlowReport>('/api/reports/cash-flow/daily', { params: { from, to } })); }
  async salesPdf(from: string, to: string): Promise<PdfFile> {
    try {
      const blob = await firstValueFrom(this.http.get('/api/reports/sales/overview.pdf', { params: { from, to }, responseType: 'blob' }));
      return { blob, fileName: salesPdfName(from, to) };
    } catch (error) { throw await readableError(error); }
  }
}

/** Hands a downloaded file to the browser. */
export function saveFile(file: PdfFile): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url; link.download = file.fileName; link.style.display = 'none';
  document.body.appendChild(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
