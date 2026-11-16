import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { supportGuard } from '../../core/guards';
import { Support } from './support';
import { BackupStatus, SupportApi } from './support-api';

const status: BackupStatus = {
  serviceEnabled: true, intervalMinutes: 30,
  lastBackup: { id: 'e2', startedAt: '2026-11-15T19:00:00Z', finishedAt: '2026-11-15T19:00:05Z', trigger: 'PROGRAMADO', result: 'EXITOSO', sizeBytes: 195_400_000, verified: true, error: null },
  lastSuccessfulBackup: { id: 'e2', startedAt: '2026-11-15T19:00:00Z', finishedAt: '2026-11-15T19:00:05Z', trigger: 'PROGRAMADO', result: 'EXITOSO', sizeBytes: 195_400_000, verified: true, error: null },
  lastExternalCopy: { id: 'c2', executionId: 'e2', destinationType: 'EXTERNO_CIFRADO', completedAt: '2026-11-15T18:30:00Z', state: 'COMPLETADA', encrypted: true, verified: true, error: null },
  destinations: [
    { type: 'LOCAL_FISICO_SEPARADO', name: 'Copia local separada', path: 'E:\\Respaldos', active: true, encrypted: false, passphraseConfigured: true, dailyRetention: 7, weeklyRetention: 4, monthlyRetention: 12, freeBytes: 197_568_495_616, totalBytes: 253_000_000_000, lastCopy: null, version: 2 },
    { type: 'EXTERNO_CIFRADO', name: 'Copia externa cifrada', path: null, active: false, encrypted: true, passphraseConfigured: true, dailyRetention: 7, weeklyRetention: 4, monthlyRetention: 12, freeBytes: null, totalBytes: null, lastCopy: null, version: null },
  ],
  recentEvents: [
    { at: '2026-11-15T19:00:00Z', kind: 'RESPALDO', description: 'Respaldo automático', result: 'EXITOSO' },
    { at: '2026-11-15T18:30:00Z', kind: 'COPIA', description: 'Envío de copia externa', result: 'FALLIDA' },
  ],
};

describe('Soporte y continuidad', () => {
  const api = { status: vi.fn(), run: vi.fn(), updateDestination: vi.fn(), healthy: vi.fn() };
  let permissions: string[];

  beforeEach(() => {
    vi.clearAllMocks(); permissions = ['BACKUPS.MANAGE'];
    api.status.mockResolvedValue(status); api.healthy.mockResolvedValue(true); api.run.mockResolvedValue({});
    TestBed.configureTestingModule({ providers: [
      { provide: SupportApi, useValue: api },
      { provide: SessionService, useValue: { has: (code: string) => permissions.includes(code) } },
    ] });
  });

  async function create() {
    const fixture = TestBed.createComponent(Support);
    fixture.detectChanges(); await fixture.whenStable();
    // La carga espera la salud y luego el estado: se deja terminar antes de mirar la pantalla.
    await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
    return fixture;
  }

  it('solo entra quien tiene BACKUPS.MANAGE', () => {
    expect(TestBed.runInInjectionContext(() => supportGuard({} as never, {} as never))).toBe(true);
    permissions = [];
    expect(TestBed.runInInjectionContext(() => supportGuard({} as never, {} as never))).toBe(false);
  });

  it('muestra servidor, último respaldo, copia externa, espacio y eventos como la maqueta', async () => {
    const fixture = await create();
    const c = fixture.componentInstance;
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Servicios esenciales activos');
    expect(text).toContain(c.time(status.lastSuccessfulBackup!.startedAt));
    expect(text).toContain('Verificado correctamente');
    expect(text).toContain('Sin configurar');
    expect(text).toContain('184 GB');
    expect(text).toContain('Cada 30 minutos y después del cierre.');
    expect(text).toContain('186,3 MB');
    expect(text).toContain('Verificada');
    expect(text).toContain('Respaldo automático');
    expect(text).toContain('Correcto');
    expect(text).toContain('Falló');
  });

  it('marca componentes caídos y el servicio apagado deshabilita «Crear respaldo»', async () => {
    api.healthy.mockImplementation(async (path: string) => path === '/api/health');
    api.status.mockResolvedValue({ ...status, serviceEnabled: false });
    const fixture = await create();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Con fallas');
    expect(text).toContain('No disponible');
    expect(text).toContain('Apagado');
    const create_ = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(b => b.textContent?.includes('Crear respaldo'));
    expect(create_?.disabled).toBe(true);
  });

  it('«Crear respaldo» lo pide al servidor y avisa que corre en segundo plano', async () => {
    const c = (await create()).componentInstance;
    await c.runNow();
    expect(api.run).toHaveBeenCalledTimes(1);
    expect(c.notice()).toContain('segundo plano');
  });

  it('guarda la carpeta de un destino con su versión y vuelve a cargar el estado', async () => {
    const c = (await create()).componentInstance;
    api.updateDestination.mockResolvedValue(status.destinations[0]);
    c.drafts.LOCAL_FISICO_SEPARADO.path = '  F:\\Copias  ';
    await c.saveDestination(status.destinations[0]);
    expect(api.updateDestination).toHaveBeenCalledWith('LOCAL_FISICO_SEPARADO', 'F:\\Copias', true, 2);
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('una carpeta vacía no llega al servidor y un rechazo muestra su mensaje', async () => {
    const c = (await create()).componentInstance;
    c.drafts.EXTERNO_CIFRADO.path = ' ';
    await c.saveDestination(status.destinations[1]);
    expect(api.updateDestination).not.toHaveBeenCalled();
    expect(c.error()).toContain('Indique la carpeta');
    api.updateDestination.mockRejectedValue(new HttpErrorResponse({ status: 400, error: { detail: 'La copia local debe estar en un disco distinto al de la base de datos.' } }));
    c.drafts.LOCAL_FISICO_SEPARADO.path = 'C:\\Respaldos';
    await c.saveDestination(status.destinations[0]);
    expect(c.error()).not.toBe('');
  });
});
