import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';
import { paymentCorrectionNotice, SalePaymentCorrection } from './sale-payment-correction';
import { SaleDetail, SalesApi } from './sales-api';

const sale = {
  id: 's1', visibleNumber: 'VTA-1', total: 50000, version: 3, state: 'CONFIRMADA', creditId: null,
  payments: [{ id: 'p1', methodCode: 'EFECTIVO', methodName: 'Efectivo', appliedAmount: 50000, receivedAmount: 50000, change: 0, reference: null }],
} as unknown as SaleDetail;

describe('Corregir medio de pago', () => {
  const correctPayments = vi.fn();
  const open = vi.fn();
  const create = async () => {
    TestBed.configureTestingModule({ providers: [
      { provide: SalesApi, useValue: { cancellationReasons: vi.fn().mockResolvedValue([{ id: 'r1', code: 'ERROR_REGISTRO', name: 'Error de registro', displayOrder: 10 }, { id: 'r2', code: 'OTRO', name: 'Otro', displayOrder: 20 }]), correctPayments } },
      { provide: AuthorizationDialog, useValue: { open } },
      { provide: SessionService, useValue: { user: () => ({ userName: 'admin' }) } },
    ] });
    const fixture = TestBed.createComponent(SalePaymentCorrection);
    fixture.componentRef.setInput('sale', sale);
    await fixture.componentInstance.ngOnInit();
    return fixture.componentInstance;
  };
  beforeEach(() => { vi.clearAllMocks(); TestBed.resetTestingModule(); });

  it('propone todo por Nequi cuando quedó en efectivo y exige motivo', async () => {
    const c = await create();
    expect(c.drafts).toEqual([{ methodCode: 'NEQUI', appliedAmount: 50000, receivedAmount: null, reference: '' }]);
    expect(c.validation()).toBe('Elige el motivo.');
    c.reasonId = 'r1';
    expect(c.validation()).toBe('');
  });

  it('no acepta lo mismo que ya estaba ni una suma distinta al total', async () => {
    const c = await create(); c.reasonId = 'r1';
    c.allWith('EFECTIVO');
    expect(c.validation()).toContain('Es igual');
    c.allWith('NEQUI'); c.drafts[0].appliedAmount = 40000;
    expect(c.validation()).toContain('sumar exactamente');
  });

  it('el motivo Otro pide observación y la referencia es opcional', async () => {
    const c = await create(); c.reasonId = 'r2';
    expect(c.validation()).toContain('observación');
    c.observation = 'Pagó por Nequi';
    expect(c.validation()).toBe('');
  });

  it('pide autorización y avisa cuánto efectivo espera ahora la caja', async () => {
    const c = await create(); c.reasonId = 'r1';
    await c.confirm();
    expect(open).toHaveBeenCalledOnce();
    expect(paymentCorrectionNotice({ saleId: 's1', visibleNumber: 'VTA-1', version: 4, cashDifference: -50000,
      payments: [{ id: 'n', methodCode: 'NEQUI', methodName: 'Nequi', appliedAmount: 50000, receivedAmount: null, change: 0, reference: null }] }))
      .toMatch(/VTA-1.*Nequi.*espera .*50\.000 menos en efectivo/);
  });
});
