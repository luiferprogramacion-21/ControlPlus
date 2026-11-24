import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { CashCorrections, movementKindLabel } from './cash-corrections';

describe('Corregir un error de caja (auditoría A.16)', () => {
  let prompt: AuthorizationPrompt | null;
  let roles: string[];
  const expense = { id: 'm1', type: 'EGRESO', category: 'GASTO', amount: 20000, concept: 'Almuerzo', occurredAtUtc: '2026-11-24T15:00:00Z' };
  const create = () => {
    const fixture = TestBed.createComponent(CashCorrections);
    fixture.componentRef.setInput('state', { currentShift: { version: 3 }, ownOperatorSession: null } as never);
    fixture.componentRef.setInput('movements', [expense, { id: 'v1', type: 'INGRESO', category: 'VENTA', amount: 5000, concept: null, occurredAtUtc: '2026-11-24T15:00:00Z' }] as never);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    prompt = null; roles = ['SUPERVISOR'];
    TestBed.configureTestingModule({ imports: [CashCorrections], providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: SessionService, useValue: { has: () => true, hasRole: (...codes: string[]) => codes.some(code => roles.includes(code)), user: () => ({ userName: 'super' }) } },
      { provide: AuthorizationDialog, useValue: { open: vi.fn(async (value: AuthorizationPrompt) => { prompt = value; return true; }) } }] });
  });

  it('propone el mismo movimiento, dice qué pasa con el efectivo y muestra el antes y el después', () => {
    const fixture = create(); const c = fixture.componentInstance;
    c.show(); c.selectMovement('m1'); fixture.detectChanges();
    expect(c.manualMovements.map(x => x.id)).toEqual(['m1']);
    expect([c.kind, c.amount, c.concept]).toEqual(['GASTO', 20000, 'Almuerzo']);
    c.amount = 15000; fixture.detectChanges();
    expect(c.cashEffect).toBe('El efectivo de la caja de hoy sube $5.000.');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Así quedó'); expect(text).toContain('Así quedará'); expect(text).toContain('Gasto');
    c.kind = ''; expect(c.cashEffect).toBe('El efectivo de la caja de hoy sube $20.000.');
  });

  it('no confirma si no cambió nada y explica qué hacer', async () => {
    const c = create().componentInstance;
    c.show(); c.selectMovement('m1'); c.reason = 'Error';
    await c.confirm();
    expect(c.error()).toContain('No cambiaste nada'); expect(prompt).toBeNull();
  });

  it('envía la corrección con el reemplazo y el motivo', async () => {
    const fixture = create(); const c = fixture.componentInstance; const http = TestBed.inject(HttpTestingController);
    c.show(); c.selectMovement('m1'); c.amount = 15000; c.reason = 'El gasto era de 15.000';
    await c.confirm();
    expect(prompt?.detail).toContain('sube $5.000');
    const run = prompt!.run({ userName: 'admin', password: 'clave' });
    const request = http.expectOne('/api/cash/movements/m1/correction');
    expect(request.request.body).toMatchObject({ expectedShiftVersion: 3, replacementKind: 'GASTO', replacementAmount: 15000,
      replacementConcept: 'Almuerzo', reason: 'El gasto era de 15.000', authorization: { userName: 'admin', password: 'clave' } });
    request.flush({}); await run;
    expect(c.opened()).toBe(false);
  });

  it('el Administrador también revisa el conteo de un cierre sin mover dinero', async () => {
    roles = ['ADMINISTRADOR'];
    const fixture = create(); const c = fixture.componentInstance; const http = TestBed.inject(HttpTestingController);
    c.show(); c.setMode('review');
    http.expectOne('/api/cash/closed-shifts').flush([{ id: 's1', version: 2, operatingDate: '2026-11-23', closedAtUtc: '2026-11-23T23:00:00Z', cashExpected: 100000, cashCounted: 90000 }]);
    await Promise.resolve();
    c.shiftId = 's1'; void c.loadReviews();
    http.expectOne('/api/cash/shifts/s1/reviews').flush([]);
    c.reviewedCount = 100000; fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Se contaron'); expect(text).toContain('El conteo de un cierre quedó mal');
    expect(movementKindLabel('SANGRIA')).toBe('Retiro de efectivo');
  });
});
