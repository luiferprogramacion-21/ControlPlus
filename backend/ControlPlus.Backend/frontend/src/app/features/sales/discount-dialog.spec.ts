import { TestBed } from '@angular/core/testing';
import { DiscountApplied, DiscountDialog } from './discount-dialog';
import { Discount } from './sale-discount';

describe('DiscountDialog', () => {
  async function open(inputs: { scope?: 'product' | 'sale'; gross?: number; maxPercent?: number; current?: Discount | null; reason?: string; costFloor?: number | null } = {}) {
    const fixture = TestBed.createComponent(DiscountDialog);
    if (inputs.costFloor !== undefined) fixture.componentRef.setInput('costFloor', inputs.costFloor);
    fixture.componentRef.setInput('scope', inputs.scope ?? 'product'); fixture.componentRef.setInput('gross', inputs.gross ?? 20000);
    fixture.componentRef.setInput('maxPercent', inputs.maxPercent ?? 20);
    if (inputs.current !== undefined) fixture.componentRef.setInput('current', inputs.current);
    if (inputs.reason) fixture.componentRef.setInput('currentReason', inputs.reason);
    const applied: DiscountApplied[] = []; let removed = 0; let closed = 0;
    fixture.componentInstance.applied.subscribe(x => applied.push(x)); fixture.componentInstance.removed.subscribe(() => removed++); fixture.componentInstance.closed.subscribe(() => closed++);
    fixture.detectChanges(); await fixture.whenStable();
    return { c: fixture.componentInstance, applied, removed: () => removed, closed: () => closed, fixture };
  }

  it('no deja descontar por debajo del costo aunque el rol permita el 100 % (pruebas del 26/11/2026)', async () => {
    const { c, applied } = await open({ maxPercent: 100, costFloor: 6000 });
    expect(c.limitText).toContain('por debajo del costo');
    c.value = 100; c.apply();
    expect(applied).toEqual([]); expect(c.error).toContain('por debajo del costo');
    c.value = 30; c.apply();
    expect(applied[0].discount).toEqual({ type: 'PORCENTAJE', value: 30 });
  });

  it('el título depende de si es del producto o de la venta total', async () => {
    expect((await open({ scope: 'product' })).c.title).toBe('Descuento del producto');
    expect((await open({ scope: 'sale' })).c.title).toBe('Descuento de la venta total');
  });

  it('aplica un porcentaje con su motivo', async () => {
    const { c, applied } = await open(); c.value = 10; c.reason = 'Empaque dañado'; c.apply();
    expect(applied).toEqual([{ discount: { type: 'PORCENTAJE', value: 10 }, reason: 'Empaque dañado' }]);
  });

  it('aplica un valor en pesos cambiando de modalidad', async () => {
    const { c, applied } = await open(); c.setType('VALOR_FIJO'); c.value = 1500; c.reason = 'Cliente frecuente'; c.apply();
    expect(applied[0].discount).toEqual({ type: 'VALOR_FIJO', value: 1500 });
  });

  it('cambiar de modalidad borra el valor para no confundir 10 % con $ 10', async () => {
    const { c } = await open(); c.value = 10; c.setType('VALOR_FIJO'); expect(c.value).toBeNull(); c.setType('VALOR_FIJO'); c.value = 7; c.setType('VALOR_FIJO'); expect(c.value).toBe(7);
  });

  it('muestra en vivo el descuento y lo que queda por pagar', async () => {
    const { c } = await open({ gross: 20000 }); c.value = 25; c.setType('VALOR_FIJO'); c.value = 4000;
    expect(c.amount).toBe(4000); c.setType('PORCENTAJE'); c.value = 10; expect(c.amount).toBe(2000);
  });

  it.each([
    ['sin valor', null, 'PORCENTAJE', 'Fíjate|Escribe'],
    ['sobre el límite del rol', 30, 'PORCENTAJE', 'hasta 20 %'],
    ['en pesos sobre el límite', 5000, 'VALOR_FIJO', 'máximo'],
  ])('rechaza %s', async (_n, value, type, text) => {
    const { c, applied } = await open({ gross: 20000, maxPercent: 20 }); c.setType(type as 'PORCENTAJE' | 'VALOR_FIJO'); c.value = value; c.apply();
    expect(applied).toEqual([]); expect(c.error).toMatch(new RegExp(text));
  });

  it('el motivo es opcional: se aplica sin él', async () => {
    const { c, applied } = await open(); c.value = 5; c.reason = '  '; c.apply();
    expect(applied).toEqual([{ discount: { type: 'PORCENTAJE', value: 5 }, reason: '' }]); expect(c.error).toBe('');
  });

  it('al editar carga el descuento actual y permite quitarlo', async () => {
    const { c, removed, fixture } = await open({ current: { type: 'VALOR_FIJO', value: 2000 }, reason: 'Promo' });
    expect([c.type, c.value, c.reason]).toEqual(['VALOR_FIJO', 2000, 'Promo']);
    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(b => b.textContent?.trim());
    expect(buttons).toContain('Quitar descuento');
    (Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(b => b.textContent?.trim() === 'Quitar descuento') as HTMLButtonElement).click();
    expect(removed()).toBe(1);
  });

  it('sin descuento previo no ofrece quitar', async () => {
    const { fixture } = await open();
    expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(b => b.textContent?.trim())).not.toContain('Quitar descuento');
  });
});
