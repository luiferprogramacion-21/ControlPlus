import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { TicketPrinter } from '../sales/ticket';
import { AdminApi, AdminUser, IssuedCredential } from './admin-api';
import { buildCredentialCardHtml } from './credential-card';
import { buildPdfFromJpeg, CredentialCardExporter } from './credential-card-export';
import { UserCredential } from './user-credential';

describe('Carné de operador', () => {
  const user: AdminUser = { id: 'u1', userName: 'supervisor', displayName: 'Ana <Supervisora>', isActive: true, isLocked: false,
    failedLoginAttempts: 0, lockedAtUtc: null, lastLoginAtUtc: null, roles: [{ id: 'r', code: 'SUPERVISOR', name: 'Supervisor', level: 2 } as never] };
  const issued: IssuedCredential = { credentialId: 'c1', userId: 'u1', format: 'CODE128', credentialToken: '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF',
    visibleFragment: '456789AB'.slice(0, 8), issuedAtUtc: '2026-11-10T15:00:00Z', expiresAtUtc: null };
  const issueCredential = vi.fn(); const print = vi.fn();
  const jpeg = vi.fn(); const pdf = vi.fn(); const save = vi.fn();

  const create = () => {
    TestBed.configureTestingModule({ providers: [{ provide: AdminApi, useValue: { issueCredential } }, { provide: TicketPrinter, useValue: { print } }, { provide: CredentialCardExporter, useValue: { jpeg, pdf, save } }] });
    const fixture = TestBed.createComponent(UserCredential);
    fixture.componentRef.setInput('user', user);
    return fixture.componentInstance;
  };
  beforeEach(() => { vi.clearAllMocks(); issueCredential.mockResolvedValue(issued); print.mockResolvedValue(undefined);
    jpeg.mockResolvedValue(new Blob(['j'])); pdf.mockResolvedValue(new Blob(['p'])); });

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
    const html = buildCredentialCardHtml({ displayName: 'Luis', userName: 'luis', roleName: 'Cajero', token: 'ABC123', fragment: 'ABC123' });
    expect(html).toContain('Baratísimo');
    expect(html).toContain('Cajero · luis');
  });
});
