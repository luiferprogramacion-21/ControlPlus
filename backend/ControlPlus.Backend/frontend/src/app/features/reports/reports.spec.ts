import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { reportsGuard } from '../../core/guards';
import { ReportsApi, SalesOverview } from './reports-api';
import { Reports } from './reports';

const day = (date: string, salesTotal = 0, changesNetTotal = 0) => ({ date, salesTotal, changesNetTotal, netSalesTotal: salesTotal + changesNetTotal });
const overview = (extra: Partial<SalesOverview> = {}): SalesOverview => ({
  from: '2026-11-01', to: '2026-11-02', currency: 'COP',
  sales: { transactions: 2, cashTotal: 80000, creditTotal: 20000, total: 100000 }, changes: { transactions: 1, netTotal: -10000 },
  netSalesTotal: 90000, previousSalesTotal: 80000, salesVariationPercentage: 25, averageTicket: 50000,
  estimatedCost: 54000, estimatedMargin: 36000, estimatedMarginPercentage: 40, costCompleteness: 'COMPLETE', missingCostLines: 0, missingMovementLines: 0,
  days: [day('2026-11-01', 100000), day('2026-11-02', 0, -10000)], paymentMethodsTotal: 80000,
  paymentMethods: [{ code: 'EFECTIVO', name: 'Efectivo', total: 50000, percentage: 62.5 }, { code: 'NEQUI', name: 'Nequi', total: 30000, percentage: 37.5 }],
  ...extra,
});
const emptyOverview = (): SalesOverview => overview({
  sales: { transactions: 0, cashTotal: 0, creditTotal: 0, total: 0 }, changes: { transactions: 0, netTotal: 0 }, netSalesTotal: 0, previousSalesTotal: 0,
  salesVariationPercentage: null, averageTicket: null, estimatedCost: 0, estimatedMargin: 0, estimatedMarginPercentage: null,
  days: [day('2026-11-01'), day('2026-11-02')], paymentMethodsTotal: 0, paymentMethods: [],
});
const cashFlow = () => ({ from: '2026-11-01', to: '2026-11-01', openingBalance: 5000, paymentTotal: 70000, manualTotal: -20000, days: [{ date: '2026-11-01', openingBalance: 5000, paymentTotal: 70000, manualTotal: -20000,
  payments: [{ date: '2026-11-01', origin: 'VENTA_CONTADO', method: 'EFECTIVO', amount: 70000 }], manualMovements: [{ date: '2026-11-01', origin: 'GASTO_MANUAL', method: null, amount: -20000 }] }] });

describe('Reportes', () => {
  const api = { salesOverview: vi.fn(), cashFlow: vi.fn(), salesPdf: vi.fn() };
  let permissions: string[];
  const session = { has: (code: string) => permissions.includes(code), hasRole: () => true, user: () => ({}) };
  let anchorName = ''; let clicks = 0;

  async function create(range?: { from: string; to: string }) {
    const fixture = TestBed.createComponent(Reports);
    if (range) { fixture.componentInstance.from = range.from; fixture.componentInstance.to = range.to; }
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }
  const text = (fixture: { nativeElement: HTMLElement }) => (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const buttons = (fixture: { nativeElement: HTMLElement }) => Array.from(fixture.nativeElement.querySelectorAll('button')).map(button => button.textContent?.trim());

  beforeEach(() => {
    vi.clearAllMocks(); anchorName = ''; clicks = 0;
    permissions = ['REPORTS.FINANCIAL_READ', 'PRODUCTS.COSTS_READ'];
    api.salesOverview.mockResolvedValue(overview()); api.cashFlow.mockResolvedValue(cashFlow());
    api.salesPdf.mockResolvedValue({ blob: new Blob(['%PDF-']), fileName: 'reporte-ventas-2026-11-01_2026-11-02.pdf' });
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }));
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { anchorName = this.download; clicks++; });
    TestBed.configureTestingModule({ providers: [{ provide: ReportsApi, useValue: api }, { provide: SessionService, useValue: session }] });
  });
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  describe('permisos', () => {
    it('con ambos permisos ofrece Ventas y Flujo de Caja y muestra el PDF', async () => {
      const fixture = await create(); const c = fixture.componentInstance;
      expect(c.kinds.map(k => k.value)).toEqual(['sales', 'cash']);
      expect(api.salesOverview).toHaveBeenCalledTimes(1); expect(buttons(fixture)).toContain('Exportar PDF');
    });
    it('solo con REPORTS.FINANCIAL_READ no ofrece Ventas ni PDF y abre Flujo de Caja sin pedir costos', async () => {
      permissions = ['REPORTS.FINANCIAL_READ'];
      const fixture = await create(); const c = fixture.componentInstance;
      expect(c.kinds.map(k => k.value)).toEqual(['cash']); expect(api.salesOverview).not.toHaveBeenCalled(); expect(api.cashFlow).toHaveBeenCalledTimes(1);
      expect(buttons(fixture)).not.toContain('Exportar PDF'); expect(text(fixture)).toContain('Cobros netos');
      c.kind = 'sales'; await c.downloadPdf(); expect(api.salesPdf).not.toHaveBeenCalled();
    });
    it('solo con PRODUCTS.COSTS_READ no ofrece ningún reporte ni llama a la API', async () => {
      permissions = ['PRODUCTS.COSTS_READ'];
      const fixture = await create();
      expect(fixture.componentInstance.kinds).toEqual([]); expect(api.salesOverview).not.toHaveBeenCalled(); expect(api.cashFlow).not.toHaveBeenCalled();
      expect(text(fixture)).toContain('No tienes permiso para consultar reportes');
    });
    it('el guard exige REPORTS.FINANCIAL_READ', () => {
      TestBed.runInInjectionContext(() => { expect(reportsGuard({} as never, {} as never)).toBe(true); permissions = ['PRODUCTS.COSTS_READ']; expect(reportsGuard({} as never, {} as never)).toBe(false); });
    });
  });

  describe('rango', () => {
    it('no solicita fechas invertidas ni de más de 31 días', async () => {
      const fixture = await create(); const c = fixture.componentInstance; api.salesOverview.mockClear();
      c.from = '2026-11-10'; c.to = '2026-11-01'; await c.apply(); expect(c.error()).toContain('posterior');
      c.from = '2026-10-01'; c.to = '2026-11-01'; await c.apply(); expect(c.error()).toContain('31 días');
      c.from = ''; await c.apply(); expect(c.error()).toContain('Indica');
      expect(api.salesOverview).not.toHaveBeenCalled();
      fixture.detectChanges(); expect(fixture.nativeElement.querySelector('button[type=submit]').disabled).toBe(true);
    });
    it('un rango de 31 días se envía tal cual', async () => {
      const fixture = await create({ from: '2026-10-01', to: '2026-10-31' });
      expect(api.salesOverview).toHaveBeenCalledWith('2026-10-01', '2026-10-31'); expect(fixture.componentInstance.error()).toBe('');
    });
    it('conserva el error del servidor (400 validation.failed) y permite reintentar', async () => {
      api.salesOverview.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { code: 'validation.failed', errors: { request: ['El rango admite hasta 31 días.'] } } }));
      const fixture = await create();
      expect(fixture.componentInstance.error()).toBe('El rango admite hasta 31 días.'); expect(text(fixture)).toContain('Reintentar');
      await fixture.componentInstance.apply(); expect(fixture.componentInstance.error()).toBe(''); expect(fixture.componentInstance.shown()).toBe('sales');
    });
    it('un 403 o 404 del servidor se muestra sin datos', async () => {
      api.salesOverview.mockRejectedValueOnce(new HttpErrorResponse({ status: 403, error: { code: 'authorization.forbidden' } }));
      const fixture = await create(); expect(fixture.componentInstance.error()).toBe('No tienes permiso para esta operación.'); expect(fixture.componentInstance.shown()).toBeNull();
    });
  });

  describe('Ventas', () => {
    it('agrupa conteos y conserva el importe completo en las barras, también en millones', async () => {
      api.salesOverview.mockResolvedValue(overview({
        sales: { transactions: 1234, cashTotal: 1234567, creditTotal: 0, total: 1234567 },
        changes: { transactions: 12345, netTotal: -10000 }, days: [day('2026-11-01', 1234567)],
      }));
      const fixture = await create(); const content = text(fixture);
      expect(fixture.nativeElement.querySelectorAll('.report-card strong')[1].textContent).toBe('1.234');
      expect(content).toContain('Cambios de productos (12.345)');
      expect(fixture.nativeElement.querySelector('.bar__value')?.textContent).toMatch(/\$\s?1\.234\.567/);
      expect(fixture.componentInstance.salesBars()[0].label).not.toMatch(/mil|mill|M/);
    });
    it('muestra los indicadores, la serie y solo los medios reales de la API', async () => {
      const fixture = await create(); const content = text(fixture);
      expect(content).toContain('Ventas'); expect(content).toMatch(/\$\s?100\.000/); expect(content).toContain('Ventas realizadas'); expect(content).toMatch(/Venta promedio: \$\s?50\.000/);
      expect(content).toMatch(/Costo productos\s*\$\s?54\.000/); expect(content).toMatch(/Ganancia estimada\s*\$\s?36\.000/); expect(content).toContain('+25 % frente al período anterior');
      expect(content).toContain('Efectivo'); expect(content).toContain('Nequi'); expect(content).not.toContain('Tarjeta');
      expect(content).toContain('62,5 %'); expect(fixture.componentInstance.salesBars().map(b => b.height)).toEqual([100, 0]);
      expect(content).toContain('Cambios de productos (1)'); expect(content).toContain('Total vendido'); expect(content).not.toContain('Ventas netas');
    });
    it('costo incompleto: costo y margen aparecen como «No disponible» y no como cero', async () => {
      api.salesOverview.mockResolvedValue(overview({ costCompleteness: 'INCOMPLETE', estimatedCost: null, estimatedMargin: null, estimatedMarginPercentage: null, missingCostLines: 2, missingMovementLines: 1 }));
      const content = text(await create());
      expect(content).toMatch(/Costo productos\s*No disponible/); expect(content).toMatch(/Ganancia estimada\s*No disponible/);
      expect(content).toContain('Faltan costos históricos en 3 líneas'); expect(content).not.toMatch(/Costo productos\s*\$\s?0/);
    });
    it('ticket y variación nulos se muestran como no disponibles', async () => {
      api.salesOverview.mockResolvedValue(overview({ averageTicket: null, salesVariationPercentage: null, previousSalesTotal: 0 }));
      const content = text(await create());
      expect(content).toContain('Venta promedio: No disponible'); expect(content).toContain('Sin período anterior con ventas para comparar');
    });
    it('período sin ventas ni cambios: ceros, mensaje vacío y todos los días listados', async () => {
      api.salesOverview.mockResolvedValue(emptyOverview());
      const fixture = await create(); const content = text(fixture);
      expect(fixture.componentInstance.salesEmpty()).toBe(true); expect(content).toContain('No hay ventas ni cambios en el período seleccionado');
      expect(content).toContain('Sin cobros inmediatos de contado'); expect(content).toMatch(/Venta promedio: No disponible/);
      expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(2); expect(fixture.nativeElement.querySelector('.bars')).toBeNull();
    });
    it('un día vacío dentro de un período con ventas conserva su fila y su barra en cero', async () => {
      const fixture = await create();
      expect(fixture.nativeElement.querySelectorAll('.bar').length).toBe(2); expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(2);
      expect(fixture.componentInstance.salesBars()[1].height).toBe(0);
    });
    it('una carga más lenta de un pedido anterior no pisa el resultado nuevo', async () => {
      let release!: (value: SalesOverview) => void;
      api.salesOverview.mockReturnValueOnce(new Promise<SalesOverview>(resolve => { release = resolve; }));
      const fixture = TestBed.createComponent(Reports); fixture.detectChanges();
      api.salesOverview.mockResolvedValueOnce(overview({ sales: { transactions: 9, cashTotal: 1, creditTotal: 0, total: 1 } }));
      await fixture.componentInstance.apply(); release(overview()); await fixture.whenStable();
      expect(fixture.componentInstance.sales()?.sales.transactions).toBe(9);
    });
  });

  describe('descarga del PDF', () => {
    it('pide el PDF del rango aplicado y lo entrega al navegador con su nombre', async () => {
      const fixture = await create({ from: '2026-11-01', to: '2026-11-02' }); const c = fixture.componentInstance;
      await c.downloadPdf();
      expect(api.salesPdf).toHaveBeenCalledWith('2026-11-01', '2026-11-02'); expect(clicks).toBe(1); expect(anchorName).toBe('reporte-ventas-2026-11-01_2026-11-02.pdf');
      expect(c.notice()).toContain('reporte-ventas-2026-11-01_2026-11-02.pdf'); expect(c.downloading()).toBe(false);
    });
    it('no entrega nada si el PDF falla y muestra el error', async () => {
      api.salesPdf.mockRejectedValueOnce(new HttpErrorResponse({ status: 403, error: null }));
      const fixture = await create(); await fixture.componentInstance.downloadPdf();
      expect(clicks).toBe(0); expect(fixture.componentInstance.error()).toBe('No tienes permiso para esta operación.'); expect(fixture.componentInstance.downloading()).toBe(false);
    });
    it('no descarga con un rango inválido ni dos veces a la vez', async () => {
      const fixture = await create(); const c = fixture.componentInstance;
      c.to = '2025-01-01'; await c.downloadPdf(); expect(api.salesPdf).not.toHaveBeenCalled();
      c.to = '2026-11-02'; c.from = '2026-11-01'; c.downloading.set(true); await c.downloadPdf(); expect(api.salesPdf).not.toHaveBeenCalled();
    });
    it('el botón desaparece al elegir Flujo de Caja, que no tiene PDF', async () => {
      const fixture = await create(); const select = fixture.nativeElement.querySelector('#report-kind') as HTMLSelectElement; select.value = 'cash'; select.dispatchEvent(new Event('change')); fixture.detectChanges();
      expect(fixture.componentInstance.kind).toBe('cash'); expect(buttons(fixture)).not.toContain('Exportar PDF');
    });
  });

  describe('Flujo de Caja', () => {
    it('muestra totales, el desglose por origen y medio y no pide costos', async () => {
      const fixture = await create(); const c = fixture.componentInstance; c.kind = 'cash'; await c.apply(); fixture.detectChanges();
      const content = text(fixture);
      expect(api.cashFlow).toHaveBeenCalledWith(c.from, c.to); expect(content).toContain('Venta de contado'); expect(content).toContain('Gasto'); expect(content).toMatch(/-\s?\$\s?20\.000|\$\s?-20\.000|-\$\s?20\.000/);
    });
    it('un período sin movimientos muestra el estado vacío', async () => {
      permissions = ['REPORTS.FINANCIAL_READ'];
      api.cashFlow.mockResolvedValue({ from: '2026-11-01', to: '2026-11-01', openingBalance: 0, paymentTotal: 0, manualTotal: 0, days: [{ date: '2026-11-01', openingBalance: 0, paymentTotal: 0, manualTotal: 0, payments: [], manualMovements: [] }] });
      expect(text(await create())).toContain('No hay movimientos de caja en el período seleccionado');
    });
  });
});
