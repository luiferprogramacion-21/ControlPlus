import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { SalesApi } from '../sales/sales-api';
import { PrintSettings } from './print-settings';

describe('PrintSettings', () => {
  const api = { printProfiles: vi.fn(), printQueue: vi.fn(), createPrintProfile: vi.fn(), retryAdminPrintJob: vi.fn() };
  let permissions: string[];
  async function create() {
    const fixture = TestBed.createComponent(PrintSettings); fixture.detectChanges(); await fixture.whenStable(); return fixture.componentInstance;
  }
  beforeEach(() => {
    vi.clearAllMocks(); permissions = ['CONFIGURATION.MANAGE'];
    api.printProfiles.mockResolvedValue([]); api.printQueue.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    api.createPrintProfile.mockResolvedValue({}); api.retryAdminPrintJob.mockResolvedValue({});
    TestBed.configureTestingModule({ providers: [{ provide: SalesApi, useValue: api }, { provide: SessionService, useValue: { has: (c: string) => permissions.includes(c) } }] });
  });
  it('carga perfiles y cola', async () => { await create(); expect(api.printProfiles).toHaveBeenCalled(); expect(api.printQueue).toHaveBeenCalledWith(1, 20); });
  it.each([[{ name: '', systemQueueName: 'Q', columns: 32 }], [{ name: 'N', systemQueueName: '', columns: 32 }], [{ name: 'N', systemQueueName: 'Q', columns: 23 }], [{ name: 'N', systemQueueName: 'Q', columns: 81 }], [{ name: 'N', systemQueueName: 'Q', columns: 30.5 }]])('rechaza datos inválidos %j', async form => {
    const c = await create(); c.form = { ...form, makeDefault: true }; await c.save(); expect(api.createPrintProfile).not.toHaveBeenCalled(); expect(c.formError()).not.toBe('');
  });
  it('crea el perfil con los límites del contrato y avisa que la cola no se verifica', async () => {
    const c = await create(); c.form = { name: ' Recibos caja ', systemQueueName: ' COLA ', columns: 24, makeDefault: false }; await c.save();
    expect(api.createPrintProfile).toHaveBeenCalledWith({ name: 'Recibos caja', systemQueueName: 'COLA', columns: 24, makeDefault: false });
    expect(c.notice()).toContain('No se comprobó');
  });
  it('reintenta solo trabajos fallidos con permiso', async () => {
    const c = await create(); const job = { id: 'j', status: 'ERROR' } as never;
    expect(c.canRetry(job)).toBe(true); expect(c.canRetry({ id: 'j', status: 'IMPRESO' } as never)).toBe(false);
    permissions = []; expect(c.canRetry(job)).toBe(false);
    permissions = ['CONFIGURATION.MANAGE']; await c.retry(job); expect(api.retryAdminPrintJob).toHaveBeenCalledWith('j');
  });
});
