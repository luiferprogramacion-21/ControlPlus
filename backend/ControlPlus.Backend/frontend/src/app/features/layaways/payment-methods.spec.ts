import { TestBed } from '@angular/core/testing';
import { appliedTotal, cashChange, emptyDraft, PaymentDraft, PaymentMethods, toPayments, toRefunds, validateDrafts } from './payment-methods';

const draft = (extra: Partial<PaymentDraft> = {}): PaymentDraft => ({ ...emptyDraft(), appliedAmount: 1000, receivedAmount: 1000, ...extra });

describe('Medios de pago: funciones', () => {
  it('suma lo aplicado y calcula el cambio solo del efectivo', () => {
    const rows = [draft({ appliedAmount: 4000, receivedAmount: 10000 }), draft({ methodCode: 'NEQUI', appliedAmount: 2000, reference: 'r1', receivedAmount: null })];
    expect(appliedTotal(rows)).toBe(6000);
    expect(cashChange(rows)).toBe(6000);
    expect(cashChange([draft({ methodCode: 'NEQUI', appliedAmount: 500, reference: 'x' })])).toBe(0);
    expect(cashChange([draft({ appliedAmount: 5000, receivedAmount: 1000 })])).toBe(0);
  });

  it.each([
    ['sin medios', [], { mode: 'payment' as const }, 'al menos un medio'],
    ['medio repetido', [draft(), draft()], { mode: 'payment' as const }, 'una sola vez'],
    ['importe cero', [draft({ appliedAmount: 0 })], { mode: 'payment' as const }, 'entero mayor que cero'],
    ['importe decimal', [draft({ appliedAmount: 10.5 })], { mode: 'payment' as const }, 'entero mayor que cero'],
    ['supera el máximo', [draft({ appliedAmount: 6000, receivedAmount: 6000 })], { mode: 'payment' as const, max: 5000, overMessage: 'Supera el saldo.' }, 'Supera el saldo'],
    ['suma distinta a la exacta', [draft({ appliedAmount: 3000 })], { mode: 'refund' as const, exact: 4000, exactMessage: 'No suma.' }, 'No suma'],
    ['efectivo recibido menor', [draft({ appliedAmount: 5000, receivedAmount: 4000 })], { mode: 'payment' as const }, 'efectivo recibido'],
    ['devolución electrónica con referencia larga', [draft({ methodCode: 'NEQUI', reference: 'x'.repeat(201) })], { mode: 'refund' as const }, 'referencia'],
    ['electrónico con referencia larga', [draft({ methodCode: 'TRANSFERENCIA', reference: 'x'.repeat(201) })], { mode: 'refund' as const }, 'referencia'],
  ])('rechaza: %s', (_name, rows, rules, text) => {
    expect(validateDrafts(rows as PaymentDraft[], rules)).toContain(text);
  });

  it('en un abono la referencia electrónica es opcional y se envía nula (21 de noviembre de 2026)', () => {
    const rows = [draft({ methodCode: 'NEQUI', reference: ' ' })] as PaymentDraft[];
    expect(validateDrafts(rows, { mode: 'payment' })).toBe('');
    expect(toPayments(rows)[0].reference).toBeNull();
  });

  it('en reembolso el efectivo no exige recibido y acepta una suma exacta', () => {
    expect(validateDrafts([draft({ appliedAmount: 4000, receivedAmount: null })], { mode: 'refund', exact: 4000 })).toBe('');
  });

  it('acepta un abono válido con efectivo y un medio electrónico', () => {
    const rows = [draft({ appliedAmount: 3000, receivedAmount: 5000 }), draft({ methodCode: 'NEQUI', appliedAmount: 2000, reference: 'ok', receivedAmount: null })];
    expect(validateDrafts(rows, { mode: 'payment', max: 5000 })).toBe('');
  });

  it('transforma al contrato: efectivo sin referencia y electrónico sin recibido', () => {
    const rows = [draft({ appliedAmount: 3000, receivedAmount: 5000, reference: 'ignorada' }), draft({ methodCode: 'NEQUI', appliedAmount: 2000, reference: ' r-9 ', receivedAmount: 99 })];
    expect(toPayments(rows)).toEqual([
      { methodCode: 'EFECTIVO', appliedAmount: 3000, receivedAmount: 5000, reference: null },
      { methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'r-9' },
    ]);
    expect(toRefunds(rows)).toEqual([
      { methodCode: 'EFECTIVO', amount: 3000, reference: null },
      { methodCode: 'NEQUI', amount: 2000, reference: 'r-9' },
    ]);
  });
});

describe('Medios de pago: componente', () => {
  it('agrega hasta tres medios distintos y quita sin dejar la lista vacía', () => {
    const component = TestBed.runInInjectionContext(() => new PaymentMethods());
    component.drafts = [emptyDraft()];
    component.add(); component.add(); component.add();
    expect(component.drafts.map(d => d.methodCode)).toEqual(['EFECTIVO', 'TRANSFERENCIA', 'NEQUI']);
    component.remove(0); component.remove(0); component.remove(0);
    expect(component.drafts.length).toBe(1);
  });

  it('muestra la etiqueta de devolución y oculta el efectivo recibido en reembolso', () => {
    const fixture = TestBed.createComponent(PaymentMethods);
    fixture.componentRef.setInput('drafts', [emptyDraft()]);
    fixture.componentRef.setInput('mode', 'refund');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Valor a devolver');
    expect(root.textContent).not.toContain('Efectivo recibido');
    fixture.componentRef.setInput('mode', 'payment');
    fixture.detectChanges();
    expect(root.textContent).toContain('Efectivo recibido');
  });
});

/** Pruebas del usuario del 24/11/2026, punto 12: al cobrar la diferencia de un cambio se ve cuánto devolver. */
describe('Medios de pago: cambio visible', () => {
  async function render(showChange: boolean) {
    const fixture = TestBed.createComponent(PaymentMethods);
    fixture.componentInstance.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 12975, receivedAmount: 20000, reference: '' }];
    fixture.componentInstance.showChange = showChange;
    fixture.componentInstance.amountLabel = 'Valor que paga el cliente';
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }
  it('muestra el cambio en grande cuando se pide', async () => {
    const text = await render(true);
    expect(text).toContain('Valor que paga el cliente');
    expect(text).toContain('Cambio');
    expect(text.replace(/\s/g, ' ')).toMatch(/7\.025/);
  });
  it('no lo muestra donde la pantalla ya tiene su propio cambio', async () => {
    expect(await render(false)).not.toContain('Cambio');
  });
});
