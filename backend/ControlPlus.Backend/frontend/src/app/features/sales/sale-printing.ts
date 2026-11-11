import { inject, Injectable } from '@angular/core';
import { errorText } from '../../core/api';
import { SalesApi, printJobStatusText } from './sales-api';
import { buildTicketHtml, TicketPrinter } from './ticket';

export interface ReprintOutcome { message: string; status: string | null; }

/**
 * Reprints the ticket of an existing sale (from the sales history).
 * With an active printer profile the backend prints (and records the job); without one the browser prints.
 * Every reprint is traceable: it always records a reprint request or retries the failed job.
 */
@Injectable({ providedIn: 'root' })
export class SalePrinting {
  private readonly api = inject(SalesApi);
  private readonly printer = inject(TicketPrinter);

  async reprint(saleId: string): Promise<ReprintOutcome> {
    try {
      const status = await this.api.printingStatus();
      if (status.hasActiveProfile) {
        let latest = (await this.api.printJobs(saleId)).items.at(-1);
        if (latest?.status === 'PENDIENTE') return { status: latest.status, message: 'El ticket sigue pendiente. Consulta el estado antes de solicitar otra copia.' };
        if (latest?.status === 'IMPRIMIENDO') return { status: latest.status, message: 'Resultado incierto: revisa la cola de Windows antes de solicitar otra copia.' };
        if (latest?.status === 'ERROR') await this.api.retryPrintJob(saleId, latest.id);
        else await this.api.requestReprint(saleId, crypto.randomUUID());
        latest = (await this.api.printJobs(saleId)).items.at(-1);
        return latest
          ? { status: latest.status, message: `${latest.copyType === 'REIMPRESION' ? 'Reimpresión' : 'Ticket'}: ${latest.status}. ${printJobStatusText(latest)}` }
          : { status: null, message: 'La solicitud quedó registrada; consulta el estado de impresión.' };
      }
      await this.api.requestReprint(saleId, crypto.randomUUID());
      const receipt = await this.api.receipt(saleId);
      await this.printer.print(buildTicketHtml(receipt, { operator: receipt.operatorName ?? undefined, customer: receipt.customerName ?? undefined }, true));
      return { status: null, message: 'Reimpresión enviada y registrada.' };
    } catch (error) { return { status: null, message: errorText(error) }; }
  }

  /** Latest print job status of a sale, for display; null when there is none or it cannot be read. */
  async lastStatus(saleId: string): Promise<string | null> {
    try { return (await this.api.printJobs(saleId)).items.at(-1)?.status ?? null; } catch { return null; }
  }
}
