import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { Config } from './config';
import { ConfigApi, NUMBERING_KINDS, Numbering } from './config-api';
import { SettingsPanel } from './settings-panel';

const numbering = (over: Partial<Numbering> = {}): Numbering => ({ documentType: 'VENTA', series: '001', prefix: 'VTA', lastNumber: 0, nextNumber: 1, version: 1, ...over });

describe('Configuración', () => {
  const api = {
    numbering: vi.fn(), createNumbering: vi.fn(), updateNumbering: vi.fn(), changePolicy: vi.fn(), updateChangePolicy: vi.fn(),
    layawaySettings: vi.fn(), updateLayawaySettings: vi.fn(), creditSettings: vi.fn(), updateCreditSettings: vi.fn(), salesSettings: vi.fn(), updateSalesSettings: vi.fn(), reasons: vi.fn(), initializeReasons: vi.fn(),
  };
  let permissions: string[];
  beforeEach(() => {
    vi.clearAllMocks(); permissions = ['CONFIGURATION.MANAGE', 'NUMBERING.MANAGE'];
    api.numbering.mockResolvedValue(null);
    api.changePolicy.mockResolvedValue({ days: 15, version: 3 });
    api.layawaySettings.mockResolvedValue({ minimumPercentage: 20, defaultDays: 15, requireMinimumInitial: true, version: 2 });
    api.creditSettings.mockResolvedValue({ defaultDays: 30, defaultMonths: null, version: 1, blockRiskyCredit: false });
    api.salesSettings.mockResolvedValue({ wholesaleMinimumQuantity: 12, version: 1 });
    api.reasons.mockResolvedValue([]);
    TestBed.configureTestingModule({ providers: [{ provide: ConfigApi, useValue: api }, { provide: SessionService, useValue: { has: (p: string) => permissions.includes(p) } }] });
  });


  describe('SettingsPanel', () => {
    async function open() { const f = TestBed.createComponent(SettingsPanel); f.detectChanges(); await f.whenStable(); return f.componentInstance; }

    it('carga el plazo predeterminado del crédito y lo guarda con la versión esperada', async () => {
      const c = await open();
      expect([c.creditDays, c.creditChooseEachTime, c.creditChanged(c.credit()!)]).toEqual([30, false, false]);
      api.updateCreditSettings.mockResolvedValue({ defaultDays: 45, defaultMonths: null, version: 2, blockRiskyCredit: false });
      c.creditDays = 45; expect(c.creditChanged(c.credit()!)).toBe(true); await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(45, 1, false, null); expect(c.creditNotice()).toContain('créditos nuevos'); expect(c.credit()?.version).toBe(2);
    });

    it('permite configurar tres meses calendario y carga de nuevo esa unidad', async () => {
      const c = await open();
      c.creditUnit = 'months'; c.creditMonths = 3;
      expect(c.creditChanged(c.credit()!)).toBe(true);
      api.updateCreditSettings.mockResolvedValue({ defaultDays: null, defaultMonths: 3, version: 2, blockRiskyCredit: false });
      await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(null, 1, false, 3);
      expect(c.creditUnit).toBe('months'); expect(c.creditMonths).toBe(3);
      expect(c.creditChooseEachTime).toBe(false); expect(c.creditChanged(c.credit()!)).toBe(false);
    });

    it.each([[0], [7], [1.5], ['']])('rechaza %s meses de crédito', async value => {
      const c = await open(); c.creditUnit = 'months'; c.creditMonths = value as number;
      expect(c.validateCredit()).toContain('entre 1 y 6');
      await c.saveCredit(); expect(api.updateCreditSettings).not.toHaveBeenCalled();
    });

    it('«elegir el vencimiento en cada crédito» envía plazo nulo y conserva el valor', async () => {
      const c = await open(); api.updateCreditSettings.mockResolvedValue({ defaultDays: null, defaultMonths: null, version: 2, blockRiskyCredit: false });
      c.creditChooseEachTime = true; await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(null, 1, false, null); expect(c.creditChooseEachTime).toBe(true); expect(c.creditChanged(c.credit()!)).toBe(false);
    });

    it.each([[0], [181], [2.5], ['']])('rechaza el plazo de crédito %s', async value => {
      const c = await open(); c.creditDays = value as number; c.creditChooseEachTime = false;
      expect(c.validateCredit()).toContain('entre 1 y 180'); await c.saveCredit(); expect(api.updateCreditSettings).not.toHaveBeenCalled();
    });

    it('un conflicto de versión muestra el error y recarga lo vigente', async () => {
      const c = await open(); api.updateCreditSettings.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'concurrency.conflict' } }));
      api.creditSettings.mockResolvedValue({ defaultDays: 20, defaultMonths: null, version: 5, blockRiskyCredit: false }); c.creditDays = 45; await c.saveCredit();
      expect(c.creditError()).not.toBe(''); expect(c.credit()).toEqual({ defaultDays: 20, defaultMonths: null, version: 5, blockRiskyCredit: false });
    });

    it('activa el bloqueo de créditos a morosos o sobre el cupo sin cambiar el plazo', async () => {
      const c = await open(); expect(c.creditBlock).toBe(false);
      api.updateCreditSettings.mockResolvedValue({ defaultDays: 30, defaultMonths: null, version: 2, blockRiskyCredit: true });
      c.creditBlock = true; expect(c.creditChanged(c.credit()!)).toBe(true); await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(30, 1, true, null);
      expect(c.creditBlock).toBe(true); expect(c.creditChanged(c.credit()!)).toBe(false);
    });

    it('guarda el mínimo de unidades para precio mayorista con la versión esperada', async () => {
      const c = await open(); expect(c.wholesaleMinimum).toBe(12);
      api.updateSalesSettings.mockResolvedValue({ wholesaleMinimumQuantity: 6, version: 2 });
      c.wholesaleMinimum = 6; await c.saveSales();
      expect(api.updateSalesSettings).toHaveBeenCalledWith(6, 1, 5, 20); expect(c.sales()).toEqual({ wholesaleMinimumQuantity: 6, version: 2 });
      expect(c.salesNotice()).toContain('Guardado');
    });

    it.each([[0], [1000], [2.5], ['']])('rechaza el mínimo mayorista %s sin llamar a la API', async value => {
      const c = await open(); c.wholesaleMinimum = value as number;
      expect(c.validateSales()).toContain('entre 1 y 999'); await c.saveSales(); expect(api.updateSalesSettings).not.toHaveBeenCalled();
    });

    it('si no se puede leer el mínimo mayorista, el resto del panel sigue funcionando', async () => {
      api.salesSettings.mockRejectedValue(new HttpErrorResponse({ status: 500 }));
      const c = await open();
      expect(c.sales()).toBeNull(); expect(c.salesLoadError()).not.toBe(''); expect(c.credit()).not.toBeNull();
    });

    it('si no se puede leer el plazo del crédito, el resto del panel sigue funcionando', async () => {
      api.creditSettings.mockRejectedValue(new HttpErrorResponse({ status: 500 }));
      const c = await open();
      expect(c.credit()).toBeNull(); expect(c.creditLoadError()).not.toBe(''); expect(c.layaway()).not.toBeNull(); expect(c.policy()).not.toBeNull();
    });

    it('carga el plazo de cambios y los parámetros de apartados', async () => {
      const c = await open();
      expect([c.days, c.percentage, c.layawayDays, c.chooseEachDate]).toEqual([15, 20, 15, false]);
    });

    it('guarda el plazo con la versión esperada y avisa que aplica a ventas nuevas', async () => {
      const c = await open(); api.updateChangePolicy.mockResolvedValue({ days: 30, version: 4 });
      c.days = 30; await c.savePolicy();
      expect(api.updateChangePolicy).toHaveBeenCalledWith(30, 3); expect(c.policy()?.version).toBe(4); expect(c.policyNotice()).toContain('nuevas');
    });

    it('rechaza días no enteros o menores que uno', async () => {
      const c = await open(); for (const bad of [0, -3, 2.5]) { c.days = bad; await c.savePolicy(); expect(c.policyError()).not.toBe(''); }
      expect(api.updateChangePolicy).not.toHaveBeenCalled();
    });

    it('un conflicto de versión muestra el error y recarga el valor vigente', async () => {
      const c = await open(); api.updateChangePolicy.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'concurrency.conflict' } }));
      api.changePolicy.mockResolvedValue({ days: 22, version: 9 });
      c.days = 30; await c.savePolicy();
      expect(c.policyError()).not.toBe(''); expect(c.policy()).toEqual({ days: 22, version: 9 });
    });

    it('guarda porcentaje y plazo de apartados; con «elegir fecha» envía plazo nulo', async () => {
      const c = await open(); api.updateLayawaySettings.mockResolvedValue({ minimumPercentage: 25.5, defaultDays: 10, requireMinimumInitial: true, version: 3 });
      c.percentage = 25.5; c.layawayDays = 10; await c.saveLayaway();
      expect(api.updateLayawaySettings).toHaveBeenCalledWith(25.5, 10, true, 2);
      api.updateLayawaySettings.mockResolvedValue({ minimumPercentage: 25.5, defaultDays: null, requireMinimumInitial: true, version: 4 });
      c.chooseEachDate = true; await c.saveLayaway();
      expect(api.updateLayawaySettings).toHaveBeenLastCalledWith(25.5, null, true, 3); expect(c.chooseEachDate).toBe(true);
    });

    it('el interruptor «Exigir pago inicial mínimo» se envía, conserva el porcentaje y habilita guardar', async () => {
      const c = await open(); const current = c.layaway()!;
      expect(c.requireMinimum).toBe(true); expect(c.layawayChanged(current)).toBe(false);
      c.requireMinimum = false; expect(c.layawayChanged(current)).toBe(true);
      api.updateLayawaySettings.mockResolvedValue({ minimumPercentage: 20, defaultDays: 15, requireMinimumInitial: false, version: 3 });
      await c.saveLayaway();
      expect(api.updateLayawaySettings).toHaveBeenCalledWith(20, 15, false, 2);
      expect(c.requireMinimum).toBe(false); expect(c.percentage).toBe(20); expect(c.layawayChanged(c.layaway()!)).toBe(false);
    });

    it('con la exigencia apagada el porcentaje sigue validándose y se conserva', async () => {
      const c = await open(); c.requireMinimum = false; c.percentage = 150;
      expect(c.validateLayaway()).toContain('no superar 100'); await c.saveLayaway(); expect(api.updateLayawaySettings).not.toHaveBeenCalled();
    });

    it.each([[0, 10, 'mayor que cero'], [101, 10, 'no superar 100'], [33.333, 10, 'dos decimales'], [20, 0, 'entre 1 y 365'], [20, 366, 'entre 1 y 365']])('rechaza porcentaje %s con plazo %s', async (percentage, days, text) => {
      const c = await open(); c.percentage = percentage; c.layawayDays = days; c.chooseEachDate = false;
      expect(c.validateLayaway()).toContain(text); await c.saveLayaway(); expect(api.updateLayawaySettings).not.toHaveBeenCalled();
    });

    it('detecta si hubo cambios para habilitar el guardado', async () => {
      const c = await open(); const current = c.layaway()!;
      expect(c.layawayChanged(current)).toBe(false); c.percentage = 30; expect(c.layawayChanged(current)).toBe(true);
    });
  });


  describe('Config', () => {
    it('la pestaña inicial depende de los permisos; ya no hay Numeración ni Motivos', () => {
      const all = TestBed.createComponent(Config).componentInstance;
      expect([all.tab(), all.canConfigure]).toEqual(['establishment', true]);
      permissions = ['CASH.SHIFT_MODE_MANAGE'];
      const onlyShift = TestBed.createComponent(Config).componentInstance;
      expect([onlyShift.tab(), onlyShift.canConfigure]).toEqual(['shift-mode', false]);
    });
  });
});
