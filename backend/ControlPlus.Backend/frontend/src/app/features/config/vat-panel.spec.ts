import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { Config } from './config';
import { ConfigApi, VatSettings } from './config-api';
import { validateVatPercentage, VatPanel } from './vat-panel';

const settings = (over: Partial<VatSettings> = {}): VatSettings => ({ enabled: false, percentage: null, version: 0, updatedAtUtc: null, ...over });

describe('IVA del negocio', () => {
  describe('ConfigApi', () => {
    let api: ConfigApi; let http: HttpTestingController;
    beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(ConfigApi); http = TestBed.inject(HttpTestingController); });
    afterEach(() => http.verify());
    it('consulta y guarda interruptor, porcentaje y versión en /api/settings/vat', async () => {
      const read = api.vatSettings(); http.expectOne('/api/settings/vat').flush(settings()); expect((await read).enabled).toBe(false);
      const save = api.updateVatSettings(true, 19, 0); const req = http.expectOne('/api/settings/vat');
      expect(req.request.method).toBe('PUT'); expect(req.request.body).toEqual({ enabled: true, percentage: 19, expectedVersion: 0 });
      req.flush(settings({ enabled: true, percentage: 19, version: 1 })); expect((await save).version).toBe(1);
    });
  });

  describe('validación', () => {
    it('activar exige un porcentaje entre 0 y 100 con hasta dos decimales', () => {
      expect(validateVatPercentage(true, null)).toContain('Indica');
      expect(validateVatPercentage(true, 0)).toContain('mayor que cero');
      expect(validateVatPercentage(true, 101)).toContain('no superar 100');
      expect(validateVatPercentage(true, 19.555)).toContain('dos decimales');
      expect(validateVatPercentage(true, 19)).toBe(''); expect(validateVatPercentage(true, '19.5')).toBe(''); expect(validateVatPercentage(true, 100)).toBe('');
    });
    it('desactivado permite dejar el porcentaje vacío pero rechaza uno inválido', () => {
      expect(validateVatPercentage(false, null)).toBe(''); expect(validateVatPercentage(false, -1)).toContain('mayor que cero');
    });
  });

  describe('VatPanel', () => {
    const api = { vatSettings: vi.fn(), updateVatSettings: vi.fn() };
    beforeEach(() => { vi.clearAllMocks(); api.vatSettings.mockResolvedValue(settings()); TestBed.configureTestingModule({ providers: [{ provide: ConfigApi, useValue: api }] }); });
    async function open() { const f = TestBed.createComponent(VatPanel); f.detectChanges(); await f.whenStable(); f.detectChanges(); return f; }

    it('carga la configuración y no deja guardar sin cambios', async () => {
      api.vatSettings.mockResolvedValue(settings({ enabled: true, percentage: 19, version: 2 }));
      const f = await open(); const c = f.componentInstance;
      expect(c.enabled).toBe(true); expect(c.percentage).toBe(19); expect(c.changed(c.current()!)).toBe(false);
      expect((f.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
    });
    it('activa con porcentaje y envía la versión vigente', async () => {
      api.updateVatSettings.mockResolvedValue(settings({ enabled: true, percentage: 19, version: 1 }));
      const c = (await open()).componentInstance; c.enabled = true; c.percentage = 19; await c.save();
      expect(api.updateVatSettings).toHaveBeenCalledWith(true, 19, 0); expect(c.notice()).toContain('activado'); expect(c.current()?.version).toBe(1);
    });
    it('desactiva conservando el porcentaje', async () => {
      api.vatSettings.mockResolvedValue(settings({ enabled: true, percentage: 19, version: 1 }));
      api.updateVatSettings.mockResolvedValue(settings({ enabled: false, percentage: 19, version: 2 }));
      const c = (await open()).componentInstance; c.enabled = false; await c.save();
      expect(api.updateVatSettings).toHaveBeenCalledWith(false, 19, 1); expect(c.notice()).toContain('desactivado');
    });
    it('no llama al servidor con un porcentaje inválido', async () => {
      const c = (await open()).componentInstance; c.enabled = true; c.percentage = null; await c.save();
      expect(api.updateVatSettings).not.toHaveBeenCalled(); expect(c.error()).toContain('Indica');
    });
    it('un conflicto de versión muestra el error y recarga lo vigente', async () => {
      api.updateVatSettings.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'concurrency.conflict' } }));
      const c = (await open()).componentInstance; c.enabled = true; c.percentage = 19;
      api.vatSettings.mockResolvedValue(settings({ enabled: true, percentage: 16, version: 5 })); await c.save();
      expect(c.error()).toContain('cambiaron'); expect(c.current()?.version).toBe(5);
    });
    it('un 403 del servidor se informa sin cambiar nada', async () => {
      api.updateVatSettings.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { code: 'authorization.forbidden' } }));
      const c = (await open()).componentInstance; c.enabled = true; c.percentage = 19; await c.save();
      expect(c.error()).toBe('No tienes permiso para esta operación.');
    });
  });

  describe('pestaña en Configuración', () => {
    let permissions: string[];
    const apiStub = { numbering: vi.fn(async () => null), changePolicy: vi.fn(), layawaySettings: vi.fn(), vatSettings: vi.fn(), reasons: vi.fn(async () => []) };
    beforeEach(() => {
      vi.clearAllMocks(); apiStub.vatSettings.mockResolvedValue(settings());
      TestBed.configureTestingModule({ providers: [{ provide: ConfigApi, useValue: apiStub }, { provide: SessionService, useValue: { has: (p: string) => permissions.includes(p) } }] });
    });
    const tabs = (f: { nativeElement: HTMLElement }) => Array.from(f.nativeElement.querySelectorAll('.tabs button')).map(b => b.textContent?.trim());
    it('solo se ofrece con TAXES.MANAGE', () => {
      permissions = ['CONFIGURATION.MANAGE', 'NUMBERING.MANAGE'];
      const without = TestBed.createComponent(Config); without.detectChanges(); expect(tabs(without)).not.toContain('IVA');
      permissions = ['CONFIGURATION.MANAGE', 'NUMBERING.MANAGE', 'TAXES.MANAGE'];
      const withIt = TestBed.createComponent(Config); withIt.detectChanges(); expect(tabs(withIt)).toContain('IVA');
    });
    it('con solo TAXES.MANAGE abre directamente el panel de IVA', async () => {
      permissions = ['TAXES.MANAGE'];
      const f = TestBed.createComponent(Config); f.detectChanges(); await f.whenStable(); f.detectChanges();
      expect(f.componentInstance.tab()).toBe('vat'); expect(tabs(f)).toEqual(['IVA']); expect(apiStub.vatSettings).toHaveBeenCalled();
    });
    it('sin TAXES.MANAGE no se pide la configuración de IVA aunque se fuerce la pestaña', async () => {
      permissions = ['CONFIGURATION.MANAGE'];
      const f = TestBed.createComponent(Config); f.componentInstance.switchTab('vat'); f.detectChanges(); await f.whenStable();
      expect(apiStub.vatSettings).not.toHaveBeenCalled();
    });
  });
});
