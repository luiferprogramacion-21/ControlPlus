import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SalesApi } from './sales-api';
import { SalePrinting } from './sale-printing';
import { TicketPrinter } from './ticket';

const job = (status: string, copyType = 'REIMPRESION') => ({ id: 'j1', saleId: 's1', copyType, copyNumber: 1, status, attempts: 1, requestedAtUtc: '', queueAcceptedAtUtc: null, error: null });

describe('SalePrinting: reimpresión desde el historial', () => {
  const api = { printingStatus: vi.fn(), printJobs: vi.fn(), retryPrintJob: vi.fn(), requestReprint: vi.fn(), receipt: vi.fn() };
  const print = vi.fn();
  let service: SalePrinting;
  beforeEach(() => {
    vi.clearAllMocks(); print.mockResolvedValue(undefined); api.requestReprint.mockResolvedValue({ status: 'SOLICITADA' });
    api.receipt.mockResolvedValue({ saleId: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '2026-11-06T15:00:00Z', lines: [], payments: [], total: 1000, change: 0, grossSubtotal: 1000, discountTotal: 0, operatorName: 'Cajero Uno', customerName: 'Ana' });
    TestBed.configureTestingModule({ providers: [{ provide: SalesApi, useValue: api }, { provide: TicketPrinter, useValue: { print } }] });
    service = TestBed.inject(SalePrinting);
  });

  describe('con perfil de impresora activo', () => {
    beforeEach(() => api.printingStatus.mockResolvedValue({ hasActiveProfile: true }));
    it('registra la reimpresión y no imprime por el navegador', async () => {
      api.printJobs.mockResolvedValueOnce({ items: [job('IMPRESO', 'ORIGINAL')] }).mockResolvedValueOnce({ items: [job('IMPRESO', 'ORIGINAL'), job('PENDIENTE')] });
      const outcome = await service.reprint('s1');
      expect(api.requestReprint).toHaveBeenCalledWith('s1', expect.any(String)); expect(print).not.toHaveBeenCalled();
      expect(outcome.status).toBe('PENDIENTE'); expect(outcome.message).toContain('Reimpresión');
    });
    it('un trabajo en error se reintenta sin crear otra solicitud', async () => {
      api.printJobs.mockResolvedValueOnce({ items: [job('ERROR', 'ORIGINAL')] }).mockResolvedValueOnce({ items: [job('PENDIENTE', 'ORIGINAL')] });
      api.retryPrintJob.mockResolvedValue({});
      await service.reprint('s1');
      expect(api.retryPrintJob).toHaveBeenCalledWith('s1', 'j1'); expect(api.requestReprint).not.toHaveBeenCalled();
    });
    it.each(['PENDIENTE', 'IMPRIMIENDO'])('con un trabajo %s no pide otra copia', async status => {
      api.printJobs.mockResolvedValue({ items: [job(status)] });
      const outcome = await service.reprint('s1');
      expect(api.requestReprint).not.toHaveBeenCalled(); expect(api.retryPrintJob).not.toHaveBeenCalled(); expect(outcome.status).toBe(status);
    });
  });

  describe('sin perfil de impresora', () => {
    beforeEach(() => api.printingStatus.mockResolvedValue({ hasActiveProfile: false }));
    it('registra la solicitud e imprime por el navegador marcando la reimpresión', async () => {
      const outcome = await service.reprint('s1');
      expect(api.requestReprint).toHaveBeenCalledWith('s1', expect.any(String));
      const html = print.mock.calls[0][0] as string;
      expect(html).toContain('REIMPRESIÓN'); expect(html).toContain('Atendió: Cajero Uno'); expect(html).toContain('Cliente: Ana');
      expect(outcome.message).toContain('registrada');
    });
    it('si falla la solicitud no imprime y devuelve el error', async () => {
      api.requestReprint.mockRejectedValue(new Error('x'));
      const outcome = await service.reprint('s1');
      expect(print).not.toHaveBeenCalled(); expect(outcome.message).not.toBe('');
    });
  });

  it('lastStatus devuelve el último trabajo o null si no se puede leer', async () => {
    api.printJobs.mockResolvedValueOnce({ items: [job('IMPRESO', 'ORIGINAL'), job('ERROR')] });
    expect(await service.lastStatus('s1')).toBe('ERROR');
    api.printJobs.mockRejectedValueOnce(new Error('x')); expect(await service.lastStatus('s1')).toBeNull();
  });
});
