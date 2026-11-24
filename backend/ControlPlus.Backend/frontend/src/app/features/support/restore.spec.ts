import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Restore } from './restore';
import { RestoreOverview, SupportApi } from './support-api';

const overview: RestoreOverview = {
  rpoMinutes: 30, rtoMinutes: 120, restoreTestDays: 90, lastTest: null, nextTestDue: '2026-11-15',
  copies: [
    { copyId: 'c3', backupAt: '2026-11-15T19:00:00Z', destinationType: 'EXTERNO_CIFRADO', sizeBytes: 195_000_000, verified: true, available: true },
    { copyId: 'c2', backupAt: '2026-11-15T18:30:00Z', destinationType: 'LOCAL_FISICO_SEPARADO', sizeBytes: 194_000_000, verified: true, available: false },
  ],
};

describe('Restauración', () => {
  const api = { restoreOverview: vi.fn(), restoreTest: vi.fn() };

  beforeEach(() => {
    vi.clearAllMocks();
    api.restoreOverview.mockResolvedValue(overview);
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SupportApi, useValue: api }] });
  });

  async function create() {
    const fixture = TestBed.createComponent(Restore);
    fixture.detectChanges(); await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
    return fixture;
  }

  it('muestra las metas de Fase 4, la prueba pendiente y las copias, como la maqueta', async () => {
    const fixture = await create();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('30 min');
    expect(text).toContain('2 horas');
    expect(text).toContain('Sin pruebas');
    expect(text).toContain('Pendiente · se debe probar cada 90 días');
    expect(text).toContain('Externo cifrado');
    expect(text).toContain('Verificado');
    expect(text).toContain('No disponible');
    expect(text).toContain('Restaurar la operación exige confirmación, verificación y registro de auditoría.');
    // Auditoría A.20: pasos sencillos y palabras de tienda, sin RPO ni RTO.
    expect(text).toContain('Pulsa «Probar restauración»'); expect(text).toContain('Se respalda cada');
    expect(text).not.toContain('RPO'); expect(text).not.toContain('RTO');
  });

  it('selecciona la copia disponible más reciente y no deja elegir una que ya no existe', async () => {
    const c = (await create()).componentInstance;
    expect(c.selectedId()).toBe('c3');
    c.select(overview.copies[1]);
    expect(c.selectedId()).toBe('c3');
  });

  it('usa puntos de miles y coma decimal en metas, plazos y resultados mostrados', async () => {
    api.restoreOverview.mockResolvedValue({ ...overview, rpoMinutes: 1234, rtoMinutes: 1234.5, restoreTestDays: 1234 });
    const fixture = await create();
    const content = fixture.nativeElement.textContent as string;
    expect(content).toContain('1.234 min');
    expect(content).toContain('1.234,5 min');
    expect(content).toContain('cada 1.234 días');
    expect(fixture.componentInstance.rto(74040)).toBe('1.234 horas');
  });

  it('«Restaurar operación» queda desactivado: la pantalla solo prueba', async () => {
    const fixture = await create();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>);
    expect(buttons.find(b => b.textContent?.trim() === 'Restaurar operación')?.disabled).toBe(true);
  });

  it('prueba la copia seleccionada, muestra el resultado y recarga la última prueba', async () => {
    const fixture = await create();
    api.restoreTest.mockResolvedValue({ id: 't1', copyId: 'c3', startedAt: '2026-11-15T19:10:00Z', finishedAt: '2026-11-15T19:11:00Z', result: 'EXITOSA', rpoObservedMinutes: 30, rtoObservedMinutes: 1.25, integrityVerified: true, applicationVerified: true, error: null });
    await fixture.componentInstance.test();
    fixture.detectChanges();
    expect(api.restoreTest).toHaveBeenCalledWith('c3');
    expect(api.restoreOverview).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).toContain('Prueba correcta');
    expect(fixture.nativeElement.textContent).toContain('Tardó 1,25 min.');
  });

  it('una prueba fallida o rechazada se informa sin perder la pantalla', async () => {
    const fixture = await create();
    api.restoreTest.mockResolvedValue({ id: 't2', copyId: 'c3', startedAt: '2026-11-15T19:10:00Z', finishedAt: null, result: 'FALLIDA', rpoObservedMinutes: null, rtoObservedMinutes: 0, integrityVerified: false, applicationVerified: false, error: 'No se pudo descifrar la copia.' });
    await fixture.componentInstance.test(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('La prueba falló: No se pudo descifrar la copia.');
    api.restoreTest.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
    await fixture.componentInstance.test();
    expect(fixture.componentInstance.error()).not.toBe('');
  });
});
