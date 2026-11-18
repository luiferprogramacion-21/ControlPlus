import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { vi } from 'vitest';
import { ShiftModePanel } from './shift-mode-panel';
import { CashApi } from '../cash/cash-api';

const state = (extra: Record<string, unknown> = {}) =>
  ({ isConfigured: true, hasOpenShift: false, defaultShiftMode: 'INDIVIDUAL', configurationVersion: 7, requiresOperatorSession: false, ...extra }) as any;

describe('Configuración: caja y turnos (maqueta 21b)', () => {
  const cash = { state: vi.fn(), updateShiftMode: vi.fn() };
  const create = async () => {
    const fixture = TestBed.createComponent(ShiftModePanel);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  };
  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ imports: [ShiftModePanel], providers: [{ provide: CashApi, useValue: cash }] });
  });

  it('muestra el modo activo y cambia a turno compartido con la versión de la configuración', async () => {
    cash.state.mockResolvedValue(state());
    cash.updateShiftMode.mockResolvedValue(state({ defaultShiftMode: 'COMPARTIDO', configurationVersion: 8 }));
    const fixture = await create();
    const panel = fixture.componentInstance;
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Turno individual'); expect(text).toContain('Turno compartido'); expect(text).toContain('Cada venta exige escanear el carné.');
    expect(panel.selected).toBe('INDIVIDUAL');
    panel.selected = 'COMPARTIDO';
    await panel.save();
    expect(cash.updateShiftMode).toHaveBeenCalledWith('COMPARTIDO', 7);
    expect(panel.selected).toBe('COMPARTIDO');
    expect(panel.notice()).toContain('Turno compartido activado');
  });

  it('con un turno abierto no deja cambiar el modo y explica por qué', async () => {
    cash.state.mockResolvedValue(state({ hasOpenShift: true }));
    const fixture = await create();
    fixture.componentInstance.selected = 'COMPARTIDO';
    await fixture.componentInstance.save();
    expect(cash.updateShiftMode).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Hay un turno abierto');
  });

  it('muestra el error del servidor sin perder la selección', async () => {
    cash.state.mockResolvedValue(state());
    cash.updateShiftMode.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'No se puede cambiar el modo mientras exista un turno abierto.' } }));
    const fixture = await create();
    fixture.componentInstance.selected = 'COMPARTIDO';
    await fixture.componentInstance.save();
    expect(fixture.componentInstance.error()).toContain('turno abierto');
    expect(fixture.componentInstance.selected).toBe('COMPARTIDO');
  });
});
