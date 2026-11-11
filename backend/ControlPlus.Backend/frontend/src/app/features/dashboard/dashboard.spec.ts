import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
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

  it('solo permite entrar con el permiso financiero efectivo', () => {
    expect(TestBed.runInInjectionContext(() => dashboardGuard({} as never, {} as never))).toBe(true);
    permissions = [];
    expect(TestBed.runInInjectionContext(() => dashboardGuard({} as never, {} as never))).toBe(false);
  });

  it('muestra fecha y cifras reales sin llamar saldo de Caja al flujo neto', async () => {
    const fixture = await create();
    const text = fixture.nativeElement.textContent as string;
    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(text).toContain('10 de noviembre de 2026');
    expect(text).toContain('100.000');
    expect(text).toContain('75.000');
    expect(text).toContain('Caja actual');
    expect(text).toContain('No disponible');
    expect(text).toContain('Ventas por hora');
    expect(text).toContain('Pendiente de definir');
    expect(text).not.toContain('3.845.600');
  });

  it('mantiene las transacciones en cero y el promedio no disponible cuando no hubo ventas', async () => {
    api.summary.mockResolvedValue({ ...summary, cashSales: { transactions: 0, total: 0 }, creditSales: { transactions: 0, total: 0 }, totalSales: { transactions: 0, total: 0 } });
    const fixture = await create();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Promedio: No disponible');
    expect(text).toContain('Transacciones');
  });

  it('ante 403 oculta cifras previas y muestra el error de permiso', async () => {
    const fixture = await create();
    api.summary.mockRejectedValue(new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    await fixture.componentInstance.refresh();
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No tienes permiso para esta operación.');
    expect(text).not.toContain('100.000');
  });
});
