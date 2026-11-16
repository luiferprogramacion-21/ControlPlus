import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { dashboardGuard } from '../../core/guards';
import { Dashboard } from './dashboard';
import { DashboardApi, DashboardSummary } from './dashboard-api';

const summary: DashboardSummary = {
  operatingDate: '2026-11-10',
  cashSales: { transactions: 2, total: 80000 },
  creditSales: { transactions: 1, total: 20000 },
  totalSales: { transactions: 3, total: 100000 },
  openingBalance: 10000,
  paymentTotal: 80000,
  manualTotal: -5000,
  netCashFlow: 75000,
  comparison: { previousDate: '2026-11-09', previousTotal: 80000, percentChange: 25 },
  currentCash: { openedAt: '2026-11-10T15:00:00Z', expectedCash: 1286000 },
  alerts: { lowStockProducts: 7, overdueCreditCustomers: 3, upcomingLayaways: 1, upcomingLayawayDays: 3 },
  hourlySales: [
    { hour: 8, transactions: 1, total: 20000 },
    { hour: 9, transactions: 0, total: 0 },
    { hour: 10, transactions: 2, total: 80000 },
  ],
  recentActivity: [
    { saleId: 'a', number: 'VTA-001-3', occurredAt: '2026-11-10T19:26:00Z', customerName: null, total: 86500, saleType: 'CONTADO', state: 'CONFIRMADA' },
    { saleId: 'b', number: 'VTA-001-2', occurredAt: '2026-11-10T19:18:00Z', customerName: 'María López', total: 154000, saleType: 'CREDITO', state: 'CONFIRMADA' },
    { saleId: 'c', number: 'VTA-001-1', occurredAt: '2026-11-10T19:02:00Z', customerName: 'Pedro Gómez', total: 12000, saleType: 'CONTADO', state: 'ANULADA' },
  ],
  externalBackup: { state: 'CORRECTO', lastAttemptAt: '2026-11-10T19:30:00Z', lastSuccessAt: '2026-11-10T19:30:00Z' },
};

describe('Dashboard', () => {
  const api = { summary: vi.fn() };
  let permissions: string[];
  const session = { has: (code: string) => permissions.includes(code) };

  beforeEach(() => {
    vi.clearAllMocks();
    permissions = ['REPORTS.FINANCIAL_READ'];
    api.summary.mockResolvedValue(summary);
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: DashboardApi, useValue: api },
      { provide: SessionService, useValue: session },
    ] });
  });

  async function create() {
    const fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const textOf = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.textContent as string;

  it('solo permite entrar con el permiso financiero efectivo', () => {
    expect(TestBed.runInInjectionContext(() => dashboardGuard({} as never, {} as never))).toBe(true);
    permissions = [];
    expect(TestBed.runInInjectionContext(() => dashboardGuard({} as never, {} as never))).toBe(false);
  });

  it('muestra fecha y cifras reales, con el efectivo esperado como Caja actual y el flujo neto aparte', async () => {
    const fixture = await create();
    const text = textOf(fixture);
    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(text).toContain('10 de noviembre de 2026');
    expect(text).toContain('100.000');
    expect(text).toContain('1.286.000');
    expect(text).toContain('Flujo neto hoy');
    expect(text).toContain('75.000');
    expect(text).toContain('Abierta desde');
    expect(text).toContain(fixture.componentInstance.time('2026-11-10T15:00:00Z'));
    expect(text).toContain('Ventas por hora');
    expect(text).not.toContain('miles de pesos');
    expect(text).not.toContain('3.845.600');
  });

  it('indica que no hay turno abierto en lugar de inventar un saldo de Caja', async () => {
    api.summary.mockResolvedValue({ ...summary, currentCash: null });
    const text = textOf(await create());
    expect(text).toContain('Sin turno abierto');
    expect(text).toContain('Flujo neto hoy');
    expect(text).not.toContain('Abierta desde');
    expect(text).not.toContain('1.286.000');
  });

  it('compara con el mismo lapso de ayer y nunca inventa un porcentaje sin ventas de ayer', async () => {
    const component = (await create()).componentInstance;
    expect(component.comparison(summary)).toEqual({ text: '+25 % frente a ayer a esta hora', tone: 'up' });
    expect(component.comparison({ ...summary, comparison: { ...summary.comparison, percentChange: -20 } }))
      .toEqual({ text: '-20 % frente a ayer a esta hora', tone: 'down' });
    expect(component.comparison({ ...summary, comparison: { ...summary.comparison, percentChange: 0 } }))
      .toEqual({ text: 'Igual que ayer a esta hora', tone: 'flat' });
    expect(component.comparison({ ...summary, comparison: { previousDate: '2026-11-09', previousTotal: 0, percentChange: null } }))
      .toEqual({ text: 'Sin ventas ayer a esta hora', tone: 'none' });
  });

  it('dibuja una barra por hora, proporcional a la hora más alta, que resalta la hora pico', async () => {
    const fixture = await create();
    expect(fixture.componentInstance.bars().map((bar) => [bar.label, bar.heightPercent, bar.peak])).toEqual([
      ['8:00', 25, false],
      ['9:00', 0, false],
      ['10:00', 100, true],
    ]);
    expect(fixture.nativeElement.querySelectorAll('.dashboard-chart__bar').length).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.dashboard-chart__bar--peak').length).toBe(1);
    expect(fixture.componentInstance.bars()[2].description).toContain('2 ventas');
  });

  it('una hora con ventas muy pequeñas sigue visible', async () => {
    api.summary.mockResolvedValue({ ...summary, hourlySales: [
      { hour: 8, transactions: 1, total: 1 },
      { hour: 9, transactions: 5, total: 1000000 },
    ] });
    const bars = (await create()).componentInstance.bars();
    expect(bars[0].heightPercent).toBe(4);
    expect(bars[1].heightPercent).toBe(100);
  });

  it('muestra los contadores de las alertas operativas y la última copia externa', async () => {
    const fixture = await create();
    const text = textOf(fixture);
    expect(text).toContain('7 productos');
    expect(text).toContain('3 clientes');
    expect(text).toContain('1 apartado');
    expect(text).toContain('Respaldo externo');
    expect(text).toContain('Confirmado');
    expect(text).not.toContain('Pendiente de definir');
    expect(text).toContain('Requieren revisión');
    const layaways = Array.from(fixture.nativeElement.querySelectorAll('.dashboard-pill') as NodeListOf<HTMLElement>)
      .find((pill) => pill.textContent?.includes('Apartados próximos'));
    expect(layaways?.title).toContain('3 días');
  });

  it('marca en rojo un envío externo fallido e indica la última copia correcta', async () => {
    api.summary.mockResolvedValue({ ...summary, externalBackup: { state: 'FALLIDO', lastAttemptAt: '2026-11-10T20:00:00Z', lastSuccessAt: '2026-11-08T19:30:00Z' } });
    const fixture = await create();
    const pill = backupPill(fixture.nativeElement);
    expect(pill.classList).toContain('dashboard-pill--danger');
    expect(pill.title).toContain('Última copia correcta:');
    expect(textOf(fixture)).toContain('Falló');
  });

  it('indica cuando la copia externa no está configurada o aún no tiene copias', async () => {
    api.summary.mockResolvedValue({ ...summary, externalBackup: { state: 'SIN_CONFIGURAR', lastAttemptAt: null, lastSuccessAt: null } });
    let fixture = await create();
    expect(backupPill(fixture.nativeElement).classList).toContain('dashboard-pill--clear');
    expect(textOf(fixture)).toContain('Sin configurar');
    api.summary.mockResolvedValue({ ...summary, externalBackup: { state: 'SIN_COPIAS', lastAttemptAt: null, lastSuccessAt: null } });
    fixture = await create();
    expect(textOf(fixture)).toContain('Sin copias todavía');
  });

  it('una copia correcta de otro día muestra también su fecha', async () => {
    const component = (await create()).componentInstance;
    const text = component.externalBackup({ ...summary, externalBackup: { state: 'CORRECTO', lastAttemptAt: '2026-11-08T15:00:00Z', lastSuccessAt: '2026-11-08T15:00:00Z' } }).text;
    expect(text).toMatch(/^Confirmado 0?8\/11 \d{2}:\d{2}$/);
  });

  it('enlaza el stock bajo a Inventario filtrado cuando se puede abrir Inventario', async () => {
    permissions = ['REPORTS.FINANCIAL_READ', 'INVENTORY.READ'];
    const fixture = await create();
    const links = Array.from(fixture.nativeElement.querySelectorAll('a.dashboard-link') as NodeListOf<HTMLAnchorElement>);
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Requieren revisión · Ver productos', '7 productos']);
    for (const link of links) expect(link.getAttribute('href')).toBe('/operacion/inventario?filtro=stock-bajo');
  });

  it('sin permiso de Inventario muestra el contador sin enlace', async () => {
    const fixture = await create();
    expect(fixture.nativeElement.querySelectorAll('a.dashboard-link').length).toBe(0);
    expect(textOf(fixture)).toContain('7 productos');
    expect(textOf(fixture)).toContain('Requieren revisión');
  });

  it('muestra "Ninguno" y no alarma cuando no hay alertas', async () => {
    api.summary.mockResolvedValue({ ...summary, alerts: { lowStockProducts: 0, overdueCreditCustomers: 0, upcomingLayaways: 0, upcomingLayawayDays: 3 } });
    const fixture = await create();
    const text = textOf(fixture);
    expect(text.match(/Ninguno/g)?.length).toBe(3 + 1);
    expect(text).not.toContain('Requieren revisión');
    expect(fixture.nativeElement.querySelectorAll('.dashboard-pill--clear').length).toBe(3);
  });

  it('lista la actividad reciente con su estado y "Consumidor final" cuando no hay cliente', async () => {
    const fixture = await create();
    const rows = Array.from(fixture.nativeElement.querySelectorAll('.dashboard-activity tbody tr') as NodeListOf<HTMLElement>);
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('VTA-001-3');
    expect(rows[0].textContent).toContain('Consumidor final');
    expect(rows[0].textContent).toContain('86.500');
    expect(rows[0].textContent).toContain('Confirmada');
    expect(rows[1].textContent).toContain('María López');
    expect(rows[1].textContent).toContain('Crédito');
    expect(rows[2].textContent).toContain('Anulada');
    expect(rows[2].querySelector('.badge--error')).not.toBeNull();
  });

  it('muestra estados vacíos cuando todavía no hubo ventas', async () => {
    api.summary.mockResolvedValue({ ...summary, hourlySales: [], recentActivity: [] });
    const fixture = await create();
    const text = textOf(fixture);
    expect(text.match(/Todavía no hay ventas registradas hoy\./g)?.length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.dashboard-chart__bar').length).toBe(0);
  });

  it('mantiene las transacciones en cero y el promedio no disponible cuando no hubo ventas', async () => {
    api.summary.mockResolvedValue({ ...summary, cashSales: { transactions: 0, total: 0 }, creditSales: { transactions: 0, total: 0 }, totalSales: { transactions: 0, total: 0 } });
    const fixture = await create();
    const text = textOf(fixture);
    expect(text).toContain('Promedio: No disponible');
    expect(text).toContain('Transacciones');
  });

  it('ante 403 oculta cifras previas y muestra el error de permiso', async () => {
    const fixture = await create();
    api.summary.mockRejectedValue(new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    await fixture.componentInstance.refresh();
    fixture.detectChanges();
    const text = textOf(fixture);
    expect(text).toContain('No tienes permiso para esta operación.');
    expect(text).not.toContain('100.000');
  });
});

function backupPill(root: HTMLElement): HTMLElement {
  return Array.from(root.querySelectorAll('.dashboard-pill') as NodeListOf<HTMLElement>)
    .find((pill) => pill.textContent?.includes('Respaldo externo'))!;
}
