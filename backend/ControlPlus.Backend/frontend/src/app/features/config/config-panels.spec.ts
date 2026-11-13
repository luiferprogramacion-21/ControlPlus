import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { Config } from './config';
import { ConfigApi, NUMBERING_KINDS, Numbering } from './config-api';
import { NumberingPanel } from './numbering-panel';
import { ReasonsPanel } from './reasons-panel';
import { SettingsPanel } from './settings-panel';

const numbering = (over: Partial<Numbering> = {}): Numbering => ({ documentType: 'VENTA', series: '001', prefix: 'VTA', lastNumber: 0, nextNumber: 1, version: 1, ...over });

describe('Configuración', () => {
  const api = {
    numbering: vi.fn(), createNumbering: vi.fn(), updateNumbering: vi.fn(), changePolicy: vi.fn(), updateChangePolicy: vi.fn(),
    layawaySettings: vi.fn(), updateLayawaySettings: vi.fn(), creditSettings: vi.fn(), updateCreditSettings: vi.fn(), reasons: vi.fn(), initializeReasons: vi.fn(),
  };
  let permissions: string[];
  beforeEach(() => {
    vi.clearAllMocks(); permissions = ['CONFIGURATION.MANAGE', 'NUMBERING.MANAGE'];
    api.numbering.mockResolvedValue(null);
    api.changePolicy.mockResolvedValue({ days: 15, version: 3 });
    api.layawaySettings.mockResolvedValue({ minimumPercentage: 20, defaultDays: 15, requireMinimumInitial: true, version: 2 });
    api.creditSettings.mockResolvedValue({ defaultDays: 30, version: 1 });
    api.reasons.mockResolvedValue([]);
    TestBed.configureTestingModule({ providers: [{ provide: ConfigApi, useValue: api }, { provide: SessionService, useValue: { has: (p: string) => permissions.includes(p) } }] });
  });

  describe('NumberingPanel', () => {
    async function open() { const f = TestBed.createComponent(NumberingPanel); f.detectChanges(); await f.whenStable(); await f.componentInstance.load(); return f.componentInstance; }

    it('lista los siete documentos y distingue configurados de sin configurar', async () => {
      api.numbering.mockImplementation(async (k: { key: string }) => k.key === 'sales' ? numbering({ lastNumber: 9, nextNumber: 10 }) : null);
      const c = await open();
      expect(c.rows()).toHaveLength(NUMBERING_KINDS.length);
      expect(c.rows().filter(r => r.numbering).map(r => r.kind.key)).toEqual(['sales']);
    });

    it('muestra el próximo número real y no ofrece ajustes cuando el servidor los bloquea', async () => {
      api.numbering.mockImplementation(async (k: { key: string }) => k.key === 'sales'
        ? numbering({ series: 'CP-2026-01', lastNumber: 13, nextNumber: 14, canCorrect: false }) : null);
      const fixture = TestBed.createComponent(NumberingPanel);
      fixture.detectChanges(); await fixture.whenStable(); await fixture.componentInstance.load(); fixture.detectChanges();
      const salesRow = fixture.nativeElement.querySelector('tbody tr') as HTMLElement;
      expect(salesRow.textContent).toContain('Próximo: VTA-CP-2026-01-14');
      expect(salesRow.textContent).toContain('Ya no editable');
      expect(salesRow.textContent).not.toContain('VTA-001-000001');
      expect(salesRow.querySelector('button')).toBeNull();
    });

    it('un error al consultar un tipo no tumba a los demás', async () => {
      api.numbering.mockImplementation(async (k: { key: string }) => { if (k.key === 'layaways') throw new HttpErrorResponse({ status: 500 }); return null; });
      const c = await open();
      expect(c.rows().find(r => r.kind.key === 'layaways')?.error).not.toBe(''); expect(c.rows().find(r => r.kind.key === 'sales')?.error).toBe('');
    });

    it('configura ventas con prefijo editable y crea el consecutivo', async () => {
      const c = await open(); api.createNumbering.mockResolvedValue(numbering());
      c.open(c.rows().find(r => r.kind.key === 'sales')!); c.prefix = ' vta '; c.first = 1;
      await c.save();
      expect(api.createNumbering).toHaveBeenCalledWith(expect.objectContaining({ key: 'sales' }), 'VTA', 1);
      expect(c.editing()).toBeNull(); expect(c.notice()).toContain('ventas');
    });

    it('los documentos de prefijo fijo rechazan otro prefijo y un primer número distinto de 1', async () => {
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'sale-changes')!;
      c.open(row); expect([c.prefix, c.first]).toEqual(['CAM', 1]);
      c.prefix = 'XYZ'; expect(c.validation(row)).toContain('CAM');
      c.prefix = 'CAM'; c.first = 5; expect(c.validation(row)).toContain('empieza en 1');
      c.first = 1; expect(c.validation(row)).toBe('');
    });

    it('configura PED con el prefijo y primer número fijos', async () => {
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'purchase-orders')!;
      c.open(row); expect([c.prefix, c.first]).toEqual(['PED', 1]);
      expect(c.preview(row)).toBe('PED-[serie]-1');
      api.createNumbering.mockResolvedValue(numbering({ documentType: 'PEDIDO_COMPRA', prefix: 'PED' }));
      await c.save();
      expect(api.createNumbering).toHaveBeenCalledWith(row.kind, 'PED', 1);
      expect(c.notice()).toContain('pedidos de compra');
    });

    it('corrige un consecutivo existente con su versión y muestra el error del servidor', async () => {
      api.numbering.mockImplementation(async (k: { key: string }) => k.key === 'sales' ? numbering({ version: 7, canCorrect: true }) : null);
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'sales')!;
      c.open(row); c.first = 100; api.updateNumbering.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'No puede modificarse después de la primera venta.' } }));
      await c.save();
      expect(api.updateNumbering).toHaveBeenCalledWith(row.kind, 'VTA', 100, 7); expect(c.formError()).toContain('primera venta'); expect(c.editing()).not.toBeNull();
    });

    it('no envía una corrección si no se cambió ningún valor', async () => {
      api.numbering.mockImplementation(async (k: { key: string }) => k.key === 'credit-payments'
        ? numbering({ prefix: 'ABO', canCorrect: true }) : null);
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'credit-payments')!;
      c.open(row); await c.save();
      expect(api.updateNumbering).not.toHaveBeenCalled();
      expect(c.formError()).toContain('No hay cambios');
      expect(c.editing()).not.toBeNull();
    });

    it('un ajuste permitido actualiza el próximo número visible', async () => {
      let nextNumber = 1;
      api.numbering.mockImplementation(async (k: { key: string }) => k.key === 'purchases'
        ? numbering({ prefix: 'CMP', series: 'CP-2026-01', lastNumber: nextNumber - 1, nextNumber, canCorrect: true }) : null);
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'purchases')!;
      c.open(row); c.first = 5;
      api.updateNumbering.mockImplementation(async () => { nextNumber = 5; return numbering({ prefix: 'CMP', nextNumber: 5 }); });
      await c.save();
      expect(api.updateNumbering).toHaveBeenCalledWith(row.kind, 'CMP', 5, row.numbering!.version);
      const updated = c.rows().find(r => r.kind.key === 'purchases')!;
      expect(updated.numbering?.nextNumber).toBe(5);
      expect(c.visibleNumber(updated.numbering!)).toBe('CMP-CP-2026-01-5');
    });

    it('valida prefijo vacío o largo y primer número no entero', async () => {
      const c = await open(); const row = c.rows().find(r => r.kind.key === 'sales')!; c.open(row);
      c.prefix = ''; expect(c.validation(row)).toContain('obligatorio');
      c.prefix = 'ABCDEFGHIJK'; expect(c.validation(row)).toContain('10');
      c.prefix = 'VTA'; c.first = 0; expect(c.validation(row)).toContain('mayor que cero');
      c.first = 1.5; expect(c.validation(row)).toContain('entero');
    });
  });

  describe('SettingsPanel', () => {
    async function open() { const f = TestBed.createComponent(SettingsPanel); f.detectChanges(); await f.whenStable(); return f.componentInstance; }

    it('carga el plazo predeterminado del crédito y lo guarda con la versión esperada', async () => {
      const c = await open();
      expect([c.creditDays, c.creditChooseEachTime, c.creditChanged(c.credit()!)]).toEqual([30, false, false]);
      api.updateCreditSettings.mockResolvedValue({ defaultDays: 45, version: 2 });
      c.creditDays = 45; expect(c.creditChanged(c.credit()!)).toBe(true); await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(45, 1); expect(c.creditNotice()).toContain('créditos nuevos'); expect(c.credit()?.version).toBe(2);
    });

    it('«elegir el vencimiento en cada crédito» envía plazo nulo y conserva el valor', async () => {
      const c = await open(); api.updateCreditSettings.mockResolvedValue({ defaultDays: null, version: 2 });
      c.creditChooseEachTime = true; await c.saveCredit();
      expect(api.updateCreditSettings).toHaveBeenCalledWith(null, 1); expect(c.creditChooseEachTime).toBe(true); expect(c.creditChanged(c.credit()!)).toBe(false);
    });

    it.each([[0], [91], [2.5], ['']])('rechaza el plazo de crédito %s', async value => {
      const c = await open(); c.creditDays = value as number; c.creditChooseEachTime = false;
      expect(c.validateCredit()).toContain('entre 1 y 90'); await c.saveCredit(); expect(api.updateCreditSettings).not.toHaveBeenCalled();
    });

    it('un conflicto de versión muestra el error y recarga lo vigente', async () => {
      const c = await open(); api.updateCreditSettings.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'concurrency.conflict' } }));
      api.creditSettings.mockResolvedValue({ defaultDays: 20, version: 5 }); c.creditDays = 45; await c.saveCredit();
      expect(c.creditError()).not.toBe(''); expect(c.credit()).toEqual({ defaultDays: 20, version: 5 });
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

  describe('ReasonsPanel', () => {
    async function open() { const f = TestBed.createComponent(ReasonsPanel); f.detectChanges(); await f.whenStable(); return f.componentInstance; }

    it('muestra qué grupos están sin inicializar y cuáles tienen motivos', async () => {
      api.reasons.mockImplementation(async (g: { key: string }) => g.key === 'sale' ? [{ id: '1', code: 'OTRO', name: 'Otro' }] : []);
      const c = await open();
      expect(c.states().find(s => s.group.key === 'sale')?.reasons).toHaveLength(1); expect(c.states().find(s => s.group.key === 'purchase')?.reasons).toEqual([]);
    });

    it('inicializar crea los faltantes y actualiza el grupo', async () => {
      const c = await open(); api.initializeReasons.mockResolvedValue([{ id: '1', code: 'A', name: 'A' }, { id: '2', code: 'B', name: 'B' }]);
      await c.initialize(c.states().find(s => s.group.key === 'purchase')!);
      expect(c.states().find(s => s.group.key === 'purchase')?.reasons).toHaveLength(2); expect(c.notice()).toContain('(2)');
    });

    it('un error al inicializar queda en el grupo sin afectar a los demás', async () => {
      const c = await open(); api.initializeReasons.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
      await c.initialize(c.states().find(s => s.group.key === 'sale')!);
      expect(c.states().find(s => s.group.key === 'sale')?.error).not.toBe(''); expect(c.states().find(s => s.group.key === 'order-close')?.error).toBe('');
    });
  });

  describe('Config', () => {
    it('la pestaña inicial y las visibles dependen de los permisos', () => {
      const all = TestBed.createComponent(Config).componentInstance;
      expect([all.tab(), all.canNumber, all.canConfigure]).toEqual(['numbering', true, true]);
      permissions = ['CONFIGURATION.MANAGE'];
      const onlyConfig = TestBed.createComponent(Config).componentInstance;
      expect([onlyConfig.tab(), onlyConfig.canNumber, onlyConfig.canConfigure]).toEqual(['settings', false, true]);
      permissions = ['NUMBERING.MANAGE'];
      const onlyNumbering = TestBed.createComponent(Config).componentInstance;
      expect([onlyNumbering.tab(), onlyNumbering.canNumber, onlyNumbering.canConfigure]).toEqual(['numbering', true, false]);
    });
  });
});
