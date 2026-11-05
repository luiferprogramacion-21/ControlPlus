import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { CashApi, Reconciliation } from './cash-api';
import { Cash } from './cash';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

describe('Cierre de caja', () => {
  const reconciliation: Reconciliation = {
    shiftId: 'shift-1', shiftVersion: 4, totalExpected: 100, totalCounted: null,
    totalDifference: null, cashDifference: null,
    paymentMethods: [{ paymentMethodId: 'cash-1', code: 'EFECTIVO', name: 'Efectivo', affectsCash: true,
      expectedAmount: 100, countedAmount: null, difference: null }],
  };
  const closed = { isConfigured: true, hasOpenShift: false, defaultShiftMode: 'INDIVIDUAL', requiresPreviousShiftClosure: false,
    requiresOperatorSession: false, guidance: null, currentShift: null, ownOperatorSession: null };
  let api: { closureDifferenceReasons: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn>; state: ReturnType<typeof vi.fn> };
  let screen: Cash;
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => { try { await prompt.run({ userName: 'supervisor', password: 'clave' }); } catch { /* La ventana conserva el error. */ } return true; }) };

  beforeEach(() => {
    api = {
      closureDifferenceReasons: vi.fn(),
      close: vi.fn().mockResolvedValue(reconciliation),
      state: vi.fn().mockResolvedValue(closed),
    };
    TestBed.configureTestingModule({ providers: [
      { provide: CashApi, useValue: api },
      { provide: SessionService, useValue: { has: (permission: string) => permission === 'CASH.SHIFT_MANAGE', user: () => ({ userName: 'operador' }) } },
      { provide: AuthorizationDialog, useValue: dialog },
    ] });
    screen = TestBed.runInInjectionContext(() => new Cash());
    screen.reconciliation.set(reconciliation);
    screen.counted = { 'cash-1': 100 };
  });

  it('muestra la modalidad configurada y no presenta un valor provisional como definitivo', () => {
    expect(screen.configuredModeLabel('COMPARTIDO')).toBe('Cargando…');
    screen.stateLoading.set(false);
    expect(screen.configuredModeLabel('COMPARTIDO')).toBe('Compartido');
    expect(screen.configuredModeLabel(null)).toBe('No disponible');
    screen.stateReadError.set(true);
    expect(screen.configuredModeLabel('INDIVIDUAL')).toBe('No disponible');
  });

  it('cierra sin diferencia, sin consultar motivos ni enviar autorización', async () => {
    await screen.close();
    expect(api.closureDifferenceReasons).not.toHaveBeenCalled();
    expect(api.close).toHaveBeenCalledWith('shift-1', {
      version: 4, paymentMethods: [{ paymentMethodId: 'cash-1', countedAmount: 100 }],
      differenceReasonId: null, differenceObservation: null, authorization: null,
    });
  });

  it('envía el motivo seleccionado y las credenciales en un cierre con diferencia', async () => {
    api.closureDifferenceReasons.mockResolvedValue([{ id: 'reason-1', code: 'FALTANTE', name: 'Faltante', isActive: true }]);
    screen.counted['cash-1'] = 90;
    await screen.loadReasons();
    screen.reasonId = 'reason-1';
    screen.differenceObservation = 'Conteo verificado';
    await screen.close();
    expect(api.close).toHaveBeenCalledWith('shift-1', {
      version: 4, paymentMethods: [{ paymentMethodId: 'cash-1', countedAmount: 90 }],
      differenceReasonId: 'reason-1', differenceObservation: 'Conteo verificado',
      authorization: { userName: 'supervisor', password: 'clave' },
    });
  });

  it('una lista vacía bloquea solo el cierre con diferencia', async () => {
    api.closureDifferenceReasons.mockResolvedValue([]);
    screen.counted['cash-1'] = 90;
    await screen.loadReasons();
    expect(screen.reasons()).toEqual([]);
    await screen.close();
    expect(api.close).not.toHaveBeenCalled();
    screen.counted['cash-1'] = 100;
    await screen.close();
    expect(api.close).toHaveBeenCalledOnce();
  });

  it('distingue un error de consulta y permite reintentar sin perder el arqueo', async () => {
    api.closureDifferenceReasons.mockRejectedValueOnce(new Error('sin respuesta'))
      .mockResolvedValueOnce([{ id: 'reason-1', code: 'FALTANTE', name: 'Faltante', isActive: true }]);
    screen.counted['cash-1'] = 90;
    await screen.loadReasons();
    expect(screen.reasons()).toBeNull();
    expect(screen.reasonsError()).toBeTruthy();
    expect(screen.counted['cash-1']).toBe(90);
    await screen.loadReasons();
    expect(screen.reasonsError()).toBe('');
    expect(screen.reasons()).toHaveLength(1);
    expect(screen.counted['cash-1']).toBe(90);
  });

  it('muestra carga mientras se consulta y conserva el conteo si falla el cierre', async () => {
    let release!: (value: { id: string; code: string; name: string; isActive: boolean }[]) => void;
    api.closureDifferenceReasons.mockReturnValue(new Promise(resolve => { release = resolve; }));
    screen.counted['cash-1'] = 90;
    const reading = screen.loadReasons();
    expect(screen.reasonsLoading()).toBe(true);
    expect(screen.reasons()).toBeNull();
    release([{ id: 'reason-1', code: 'FALTANTE', name: 'Faltante', isActive: true }]);
    await reading;
    expect(screen.reasonsLoading()).toBe(false);
    screen.reasonId = 'reason-1';
    api.close.mockRejectedValueOnce(new Error('conflicto'));
    await screen.close();
    expect(screen.counted['cash-1']).toBe(90);
    expect(screen.reasonId).toBe('reason-1');
    expect(screen.reconciliation()?.shiftVersion).toBe(4);
  });
});
