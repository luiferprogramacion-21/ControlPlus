import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { CashApi, Reconciliation } from './cash-api';
import { Cash } from './cash';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { EstablishmentService } from '../../core/establishment';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';

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
      { provide: EstablishmentService, useValue: { name: () => 'Tienda', load: () => Promise.resolve() } },
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

  it('solo se cuenta el efectivo: Nequi y transferencias se muestran del sistema y no se envían ni generan diferencia', async () => {
    const withElectronic: Reconciliation = { ...reconciliation, totalExpected: 330, paymentMethods: [
      ...reconciliation.paymentMethods,
      { paymentMethodId: 'nequi-1', code: 'NEQUI', name: 'Nequi', affectsCash: false, expectedAmount: 180, countedAmount: null, difference: null },
      { paymentMethodId: 'transfer-1', code: 'TRANSFERENCIA', name: 'Transferencia', affectsCash: false, expectedAmount: 50, countedAmount: null, difference: null },
    ] };
    screen.reconciliation.set(withElectronic);
    screen.counted = { 'cash-1': 100 };
    expect(screen.cashMethods().map(m => m.code)).toEqual(['EFECTIVO']);
    expect(screen.electronicMethods().map(m => m.code)).toEqual(['NEQUI', 'TRANSFERENCIA']);
    expect(screen.electronicTotal).toBe(230);
    expect(screen.cashExpected).toBe(100);
    expect(screen.hasValidCounts).toBe(true);
    expect(screen.difference).toBe(0);
    await screen.close();
    expect(api.closureDifferenceReasons).not.toHaveBeenCalled();
    expect(api.close).toHaveBeenCalledWith('shift-1', {
      version: 4, paymentMethods: [{ paymentMethodId: 'cash-1', countedAmount: 100 }],
      differenceReasonId: null, differenceObservation: null, authorization: null,
    });
  });

  it('un Cajero abre la caja pidiendo la autorización de un Supervisor o Administrador', async () => {
    (TestBed.inject(SessionService) as unknown as { has: (p: string) => boolean }).has = () => false;
    const open = vi.fn().mockResolvedValue({});
    (api as unknown as { open: typeof open }).open = open;
    dialog.open.mockClear(); screen.initialAmount = 50000;
    await screen.open();
    expect(dialog.open).toHaveBeenCalledOnce();
    const prompt = dialog.open.mock.calls[0][0] as AuthorizationPrompt;
    expect(prompt.title).toBe('Autorizar apertura de caja');
    expect(prompt.allowCredential).toBe(true);
    expect(open).toHaveBeenCalledWith(50000, null, { userName: 'supervisor', password: 'clave' }, 'INDIVIDUAL');
  });

  it('un Supervisor o Administrador abre la caja sin ventana de autorización', async () => {
    const open = vi.fn().mockResolvedValue({});
    (api as unknown as { open: typeof open }).open = open;
    dialog.open.mockClear(); screen.initialAmount = 20000;
    await screen.open();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(open).toHaveBeenCalledWith(20000, null, undefined, 'INDIVIDUAL');
  });

  it('quien abre elige la modalidad del turno; por defecto es individual', async () => {
    const open = vi.fn().mockResolvedValue({});
    (api as unknown as { open: typeof open }).open = open;
    expect(screen.openModeFor(null)).toBe('INDIVIDUAL');
    screen.openMode = 'COMPARTIDO'; screen.initialAmount = 10000;
    await screen.open();
    expect(open).toHaveBeenCalledWith(10000, null, undefined, 'COMPARTIDO');
    expect(screen.openMode).toBeNull();
  });
});

describe('Cajón de dinero en Caja', () => {
  const shift = { id: 'shift-1', mode: 'INDIVIDUAL', state: 'ABIERTA', operatingDate: '2026-11-19', openedAtUtc: '2026-11-19T13:00:00Z',
    initialAmount: 0, version: 1, responsibleUserId: null };
  const openState = { isConfigured: true, hasOpenShift: true, defaultShiftMode: 'INDIVIDUAL', requiresPreviousShiftClosure: false,
    requiresOperatorSession: false, guidance: null, currentShift: shift, ownOperatorSession: null };
  const confirm = vi.fn();
  let api: Record<'openDrawer' | 'movement' | 'state' | 'movements' | 'drawerStatus' | 'reconciliation', ReturnType<typeof vi.fn>>;
  let screen: Cash;

  beforeEach(() => {
    confirm.mockReset().mockResolvedValue(true);
    api = {
      openDrawer: vi.fn().mockResolvedValue({ status: 'ENVIADO' }),
      movement: vi.fn().mockResolvedValue({ id: 'm1', type: 'INGRESO', category: 'MANUAL', amount: 5000, concept: 'Base', occurredAtUtc: '', drawerStatus: 'ENVIADO' }),
      state: vi.fn().mockResolvedValue(openState),
      movements: vi.fn().mockResolvedValue({ items: [] }),
      drawerStatus: vi.fn().mockResolvedValue({ enabled: true, profileName: 'Recibos caja', canOpen: true }),
      reconciliation: vi.fn().mockResolvedValue({ shiftId: 'shift-1', shiftVersion: 1, totalExpected: 0, totalCounted: null, totalDifference: null, cashDifference: null, paymentMethods: [] }),
    };
    TestBed.configureTestingModule({ providers: [
      { provide: CashApi, useValue: api },
      { provide: SessionService, useValue: { has: (permission: string) => ['CASH.SHIFT_MANAGE', 'CASH.MOVEMENTS_MANAGE'].includes(permission), user: () => ({ userName: 'supervisor' }) } },
      { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: ConfirmationDialog, useValue: { confirm } },
      { provide: EstablishmentService, useValue: { name: () => 'Tienda', load: () => Promise.resolve() } },
    ] });
    screen = TestBed.runInInjectionContext(() => new Cash());
    screen.state.set(openState);
    screen.drawer.set({ enabled: true, profileName: 'Recibos caja', canOpen: true });
  });

  it('pide confirmación y envía el motivo con una clave nueva', async () => {
    screen.drawerReason = '  Entregar cambio  ';
    await screen.openDrawer();
    expect(confirm).toHaveBeenCalledOnce();
    expect(api.openDrawer).toHaveBeenCalledWith('Entregar cambio', expect.stringMatching(/^[0-9a-f-]{36}$/));
    expect(screen.drawerReason).toBe('');
    expect(screen.notice()).toContain('Comprueba si el cajón se abrió');
  });

  it('sin motivo, sin confirmar o sin cajón habilitado no envía la orden', async () => {
    await screen.openDrawer();
    screen.drawerReason = 'Revisar efectivo'; confirm.mockResolvedValue(false);
    await screen.openDrawer();
    confirm.mockResolvedValue(true); screen.drawer.set({ enabled: false, profileName: null, canOpen: false });
    await screen.openDrawer();
    expect(api.openDrawer).not.toHaveBeenCalled();
  });

  it('un resultado incierto bloquea otra orden hasta preparar una nueva solicitud', async () => {
    api.openDrawer.mockResolvedValue({ status: 'INCIERTO', message: 'No se pudo confirmar el envío.' });
    screen.drawerReason = 'Revisar efectivo';
    await screen.openDrawer();
    expect(screen.drawerUncertain()).toBe(true);
    expect(screen.error()).toBe('No se pudo confirmar el envío.');
    await screen.openDrawer();
    expect(api.openDrawer).toHaveBeenCalledOnce();
    screen.prepareNewDrawerOpen();
    expect(screen.drawerUncertain()).toBe(false);
    expect(screen.drawerReason).toBe('');
  });

  it('después de un movimiento de efectivo avisa el resultado del cajón', async () => {
    api.movement.mockResolvedValue({ id: 'm1', type: 'INGRESO', category: 'MANUAL', amount: 5000, concept: 'Base', occurredAtUtc: '', drawerStatus: 'FALLIDO' });
    screen.kind = 'incomes'; screen.amount = 5000; screen.concept = 'Base';
    await screen.move();
    expect(api.movement).toHaveBeenCalledWith('incomes', 5000, 'Base', null);
    expect(screen.notice()).toBe('Movimiento registrado.');
    expect(screen.drawerNotice()).toContain('no se pudo enviar la orden');
  });
});
