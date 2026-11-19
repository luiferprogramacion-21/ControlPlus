import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { TicketPrinter } from '../sales/ticket';
import { EstablishmentService } from '../../core/establishment';
import { AdminApi, AdminUser, IssuedCredential, OperatorCredential } from './admin-api';
import { buildCredentialCardHtml } from './credential-card';
import { buildPdfFromJpeg, CredentialCardExporter } from './credential-card-export';
import { UserCredential } from './user-credential';

describe('Carné de operador', () => {
  const user: AdminUser = { id: 'u1', userName: 'supervisor', displayName: 'Ana <Supervisora>', isActive: true, isLocked: false,
    failedLoginAttempts: 0, lockedAtUtc: null, lastLoginAtUtc: null, roles: [{ id: 'r', code: 'SUPERVISOR', name: 'Supervisor', level: 2 } as never] };
  const issued: IssuedCredential = { credentialId: 'c1', userId: 'u1', format: 'CODE128', credentialToken: '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF',
    visibleFragment: '456789AB'.slice(0, 8), issuedAtUtc: '2026-11-10T15:00:00Z', expiresAtUtc: null };
  const issueCredential = vi.fn(); const print = vi.fn();
  const currentCredential = vi.fn(); const revokeCredential = vi.fn();
  const jpeg = vi.fn(); const pdf = vi.fn(); const save = vi.fn();
  const confirmAction = vi.fn(); const cancelConfirmation = vi.fn();

  const create = () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction, cancel: cancelConfirmation } }, { provide: AdminApi, useValue: { issueCredential, currentCredential, revokeCredential } }, { provide: TicketPrinter, useValue: { print } }, { provide: CredentialCardExporter, useValue: { jpeg, pdf, save } }, { provide: EstablishmentService, useValue: { name: () => 'Negocio de prueba' } }] });
    const fixture = TestBed.createComponent(UserCredential);
    fixture.componentRef.setInput('user', user);
    return fixture.componentInstance;
  };
  beforeEach(() => { vi.clearAllMocks(); currentCredential.mockResolvedValue(null); revokeCredential.mockResolvedValue(undefined); issueCredential.mockResolvedValue(issued); print.mockResolvedValue(undefined);
    confirmAction.mockResolvedValue(true);
    jpeg.mockResolvedValue(new Blob(['j'])); pdf.mockResolvedValue(new Blob(['p'])); });
  afterEach(() => vi.restoreAllMocks());

  it('vuelve a mostrar el carné guardado sin emitir otro', async () => {
    currentCredential.mockResolvedValue({ ...issued, version: 3, state: 'ACTIVA' } satisfies OperatorCredential);
    const component = create();
    await component.ngOnInit();
    expect(component.issued()?.credentialToken).toBe(issued.credentialToken);
    expect(issueCredential).not.toHaveBeenCalled();
  });

  it('confirma el reemplazo en una modal y evita emitir dos veces mientras está abierta', async () => {
    let resolve!: (value: boolean) => void;
    confirmAction.mockReturnValue(new Promise<boolean>(done => resolve = done));
    const component = create(); component.current.set({ ...issued, version: 1, state: 'ACTIVA' });
    const replacing = component.issue(); await component.issue();
    expect(confirmAction).toHaveBeenCalledOnce();
    expect(confirmAction).toHaveBeenCalledWith(expect.objectContaining({ title: '¿Emitir un carné nuevo?', confirmText: 'Emitir carné' }), expect.anything());
    expect(issueCredential).not.toHaveBeenCalled();
    resolve(true); await replacing;
    expect(issueCredential).toHaveBeenCalledExactlyOnceWith('u1');
  });

  it('cancelar la modal conserva el carné anterior', async () => {
    confirmAction.mockResolvedValue(false);
    const component = create(); component.current.set({ ...issued, version: 1, state: 'ACTIVA' });
    await component.issue();
    expect(issueCredential).not.toHaveBeenCalled();
    expect(component.current()?.credentialId).toBe('c1');
  });

  it('cerrar la ventana del carné descarta también una confirmación pendiente', async () => {
    let resolve!: (value: boolean) => void;
    confirmAction.mockReturnValue(new Promise<boolean>(done => resolve = done));
    const component = create(); component.current.set({ ...issued, version: 1, state: 'ACTIVA' });
    const replacing = component.issue(); component.close(); resolve(true); await replacing;
    expect(cancelConfirmation).toHaveBeenCalled();
    expect(issueCredential).not.toHaveBeenCalled(); expect(component.current()).toBeNull();
  });

  it('olvida una consulta del código que termina después de cerrar la ventana', async () => {
    let resolve!: (value: OperatorCredential) => void;
    currentCredential.mockReturnValueOnce(new Promise<OperatorCredential>(done => resolve = done));
    const component = create(); const load = component.ngOnInit(); component.close();
    resolve({ ...issued, version: 1, state: 'ACTIVA' }); await load;
    expect(component.issued()).toBeNull(); expect(component.current()).toBeNull();
  });

  it('un carné anterior conserva los datos aunque no tenga código recuperable', async () => {
    currentCredential.mockResolvedValue({ ...issued, credentialToken: null, version: 2, state: 'ACTIVA' });
    const component = create();
    await component.ngOnInit();
    expect(component.current()?.visibleFragment).toBe(issued.visibleFragment);
    expect(component.issued()).toBeNull();
  });

  it('elimina el carné que se está viendo con su versión y olvida el código', async () => {
    currentCredential.mockResolvedValue({ ...issued, version: 3, state: 'ACTIVA' });
    const component = create();
    await component.ngOnInit(); await component.remove();
    expect(revokeCredential).toHaveBeenCalledWith('u1', 'c1', 3);
    expect(component.issued()).toBeNull(); expect(component.current()).toBeNull();
    expect(component.notice()).toBe('Carné eliminado.');
  });

  it('cancelar la eliminación conserva el carné y no llama a la API', async () => {
    confirmAction.mockResolvedValue(false);
    const component = create(); component.current.set({ ...issued, version: 1, state: 'ACTIVA' });
    await component.remove(); expect(revokeCredential).not.toHaveBeenCalled(); expect(component.current()).not.toBeNull();
  });

  it('si la eliminación falla conserva el carné visible para revisar el error', async () => {
    revokeCredential.mockRejectedValueOnce(new Error('El carné cambió.'));
    const component = create(); await component.issue(); await component.remove();
    expect(component.issued()).not.toBeNull();
    expect(component.error()).not.toBe('');
  });

  it('emite el carné y muestra el código solo en la ventana', async () => {
    const component = create();
    await component.issue();
    expect(issueCredential).toHaveBeenCalledWith('u1');
    expect(component.issued()?.credentialToken).toBe(issued.credentialToken);
  });

  it('imprime el carné con el nombre escapado y el código de barras', async () => {
    const component = create();
    await component.issue(); await component.print();
    const html = print.mock.calls[0][0] as string;
    expect(html).toContain('Ana &lt;Supervisora&gt;');
    expect(html).toContain('<svg');
    expect(html).toContain('size: 85.6mm 54mm');
    expect(component.notice()).toContain('impresión');
  });

  it('un fallo al imprimir deja el código visible y avisa', async () => {
    print.mockRejectedValueOnce(new Error('sin impresora'));
    const component = create();
    await component.issue(); await component.print();
    expect(component.error()).toContain('No se pudo abrir la impresión');
    expect(component.issued()).not.toBeNull();
  });

  it('al cerrar olvida el código y avisa a la pantalla', async () => {
    const component = create();
    const closed = vi.fn(); component.closed.subscribe(closed);
    await component.issue(); component.close();
    expect(component.issued()).toBeNull();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('un error al emitir se muestra y no deja código', async () => {
    issueCredential.mockRejectedValueOnce(new Error('rechazado'));
    const component = create();
    await component.issue();
    expect(component.error()).not.toBe('');
    expect(component.issued()).toBeNull();
  });

  it('descarga el carné como JPG y como PDF con el nombre del usuario', async () => {
    const component = create();
    await component.issue();
    await component.download('jpg'); await component.download('pdf');
    expect(save.mock.calls[0][1]).toBe('carne-supervisor.jpg');
    expect(save.mock.calls[1][1]).toBe('carne-supervisor.pdf');
    expect(component.notice()).toContain('PDF');
  });

  it('un fallo al generar el archivo se avisa sin perder el código', async () => {
    jpeg.mockRejectedValueOnce(new Error('El navegador no puede dibujar la imagen.'));
    const component = create();
    await component.issue(); await component.download('jpg');
    expect(component.error()).toContain('no puede dibujar');
    expect(save).not.toHaveBeenCalled();
    expect(component.issued()).not.toBeNull();
  });

  it('el PDF contiene la imagen JPEG, el tamaño de tarjeta y una tabla de referencias válida', () => {
    const jpegBytes = new Uint8Array([0xff, 0xd8, 0xff, 0xd9]);
    const bytes = buildPdfFromJpeg(jpegBytes, 1027, 648);
    const text = new TextDecoder('latin1').decode(bytes);
    expect(text.startsWith('%PDF-1.4')).toBe(true);
    expect(text).toContain('/DCTDecode');
    expect(text).toContain('/Width 1027 /Height 648');
    expect(text).toContain('/MediaBox [0 0 242.65 153.07]');
    const startxref = Number(/startxref\n(\d+)/.exec(text)![1]);
    expect(text.slice(startxref, startxref + 4)).toBe('xref');
    const first = Number(/xref\n0 6\n0000000000 65535 f \n(\d{10})/.exec(text)![1]);
    expect(text.slice(first, first + 7)).toBe('1 0 obj');
  });

  it('la tarjeta solo contiene los datos necesarios', () => {
    const html = buildCredentialCardHtml({ businessName: 'Negocio de prueba', displayName: 'Luis', userName: 'luis', roleName: 'Cajero', token: 'ABC123', fragment: 'ABC123' });
    expect(html).toContain('Negocio de prueba');
    expect(html).toContain('Cajero · luis');
  });
});
