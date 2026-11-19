import { ConfirmationDialog } from '../../shared/confirmation-dialog';
const confirmAction = vi.fn();
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { SalesApi } from '../sales/sales-api';
import { PrintSettings } from './print-settings';

describe('PrintSettings', () => {
  const api = { printProfiles: vi.fn(), printQueue: vi.fn(), createPrintProfile: vi.fn(), retryAdminPrintJob: vi.fn(), deletePrintProfile: vi.fn() };
  let permissions: string[];
  async function create() {
    const fixture = TestBed.createComponent(PrintSettings); fixture.detectChanges(); await fixture.whenStable(); return fixture.componentInstance;
  }
  beforeEach(() => {
    vi.clearAllMocks(); confirmAction.mockResolvedValue(true); permissions = ['CONFIGURATION.MANAGE'];
    api.printProfiles.mockResolvedValue([]); api.printQueue.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.createPrintProfile.mockResolvedValue({}); api.retryAdminPrintJob.mockResolvedValue({}); api.deletePrintProfile.mockResolvedValue({ physicallyDeleted: true });
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } }, { provide: SalesApi, useValue: api }, { provide: SessionService, useValue: { has: (c: string) => permissions.includes(c) } }] });
  });
  const base = { printerType: 'RECIBOS' as const, labelWidthMm: null, labelHeightMm: null, resolutionDpi: null };
  it('carga perfiles y cola', async () => { await create(); expect(api.printProfiles).toHaveBeenCalled(); expect(api.printQueue).toHaveBeenCalledWith(1, 20); });
  it.each([[{ name: '', systemQueueName: 'Q', columns: 32 }], [{ name: 'N', systemQueueName: '', columns: 32 }], [{ name: 'N', systemQueueName: 'Q', columns: 23 }], [{ name: 'N', systemQueueName: 'Q', columns: 81 }], [{ name: 'N', systemQueueName: 'Q', columns: 30.5 }]])('rechaza datos inválidos %j', async form => {
    const c = await create(); c.form = { ...base, ...form, makeDefault: true }; await c.save(); expect(api.createPrintProfile).not.toHaveBeenCalled(); expect(c.formError()).not.toBe('');
  });
  it('crea el perfil con los límites del contrato y avisa que la cola no se verifica', async () => {
    const c = await create(); c.form = { ...base, name: ' Recibos caja ', systemQueueName: ' COLA ', columns: 24, makeDefault: false }; await c.save();
    expect(api.createPrintProfile).toHaveBeenCalledWith({ ...base, name: 'Recibos caja', systemQueueName: 'COLA', columns: 24, makeDefault: false });
    expect(c.notice()).toContain('No se comprobó');
  });
  it('crea un perfil híbrido con tamaño de etiqueta', async () => {
    const c = await create(); c.form = { name: 'Híbrida', systemQueueName: 'COLA', columns: 32, makeDefault: true, printerType: 'HIBRIDA', labelWidthMm: 50, labelHeightMm: 30, resolutionDpi: 203 };
    await c.save(); expect(api.createPrintProfile).toHaveBeenCalledWith(expect.objectContaining({ printerType: 'HIBRIDA', labelWidthMm: 50, labelHeightMm: 30, resolutionDpi: 203 }));
  });
  it('elimina un perfil tras confirmar y recarga la lista', async () => {
    const c = await create(); const profile = { id: 'p1', name: 'Recibos caja', isDefault: true } as never;
    confirmAction.mockResolvedValue(true);
    await c.remove(profile);
    expect(confirmAction.mock.calls[0][0].message).toContain('predeterminado');
    expect(api.deletePrintProfile).toHaveBeenCalledWith('p1');
    expect(c.notice()).toBe('Perfil eliminado.');
    expect(api.printProfiles).toHaveBeenCalledTimes(2);
  });
  it('un perfil con historial se retira y la pantalla lo explica', async () => {
    api.deletePrintProfile.mockResolvedValue({ physicallyDeleted: false });
    const c = await create(); confirmAction.mockResolvedValue(true);
    await c.remove({ id: 'p1', name: 'Recibos', isDefault: false } as never);
    expect(c.notice()).toContain('historial');
  });
  it('no elimina si se cancela la confirmación, sin permiso o si el servidor lo rechaza', async () => {
    const c = await create(); const profile = { id: 'p1', name: 'Recibos', isDefault: false } as never;
    confirmAction.mockResolvedValue(false); await c.remove(profile);
    expect(api.deletePrintProfile).not.toHaveBeenCalled();
    confirmAction.mockResolvedValue(true);
    permissions = []; await c.remove(profile); expect(api.deletePrintProfile).not.toHaveBeenCalled();
    permissions = ['CONFIGURATION.MANAGE']; api.deletePrintProfile.mockRejectedValue(new Error('pendientes'));
    await c.remove(profile); expect(c.error()).not.toBe(''); expect(c.deleting()).toBe(false);
  });
  it('reintenta solo trabajos fallidos con permiso', async () => {
    const c = await create(); const job = { id: 'j', status: 'ERROR' } as never;
    expect(c.canRetry(job)).toBe(true); expect(c.canRetry({ id: 'j', status: 'IMPRESO' } as never)).toBe(false);
    permissions = []; expect(c.canRetry(job)).toBe(false);
    permissions = ['CONFIGURATION.MANAGE']; await c.retry(job); expect(api.retryAdminPrintJob).toHaveBeenCalledWith('j');
  });
});
