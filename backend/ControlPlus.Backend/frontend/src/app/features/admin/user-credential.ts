import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { errorText } from '../../core/api';
import { code128Svg } from '../../core/code128';
import { EstablishmentService } from '../../core/establishment';
import { TicketPrinter } from '../sales/ticket';
import { AdminApi, AdminUser, IssuedCredential } from './admin-api';
import { buildCredentialCardHtml, CredentialCard } from './credential-card';
import { CredentialCardExporter } from './credential-card-export';

/**
 * Issues the Code 128 credential of one user and prints it as a card. The token is returned once by the server, is shown
 * only in this window and is never stored; issuing a new one revokes the previous card.
 */
@Component({
  selector: 'cp-user-credential',
  imports: [DatePipe],
  styleUrl: './admin.scss',
  template: `
    <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="user-credential-title"><div class="module-form-sheet__backdrop" (click)="close()"></div>
      <div class="panel panel--padded module-form credential-sheet">
        <div class="panel-heading"><h2 id="user-credential-title">Carné de {{ user().displayName }}</h2></div>
        <p class="muted">{{ user().userName }} · {{ roleName() }}</p>
        @if (!issued()) {
          <p>El carné lleva un código de barras que el lector escanea para iniciar una sesión rápida de caja o para que esta persona autorice descuentos, anulaciones y demás operaciones sin escribir usuario y contraseña.</p>
          <p class="form-hint">Emitir un carné nuevo <strong>revoca el anterior</strong>. El código solo se muestra ahora: imprime el carné antes de cerrar esta ventana.</p>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" (click)="close()">Cancelar</button>
            <button class="button button--primary" type="button" [disabled]="busy() || !user().isActive" (click)="issue()">{{ busy() ? 'Emitiendo…' : 'Emitir carné' }}</button></div>
          @if (!user().isActive) { <p class="field-error">El usuario está inactivo y no puede recibir carné.</p> }
        } @else {
          <div class="credential-preview" aria-label="Vista previa del carné">
            <div class="credential-preview__head"><strong>{{ establishment.name() }}</strong><span>Credencial de operador</span></div>
            <p><b>{{ user().displayName }}</b><br><small>{{ roleName() }} · {{ user().userName }}</small></p>
            <div class="credential-preview__barcode" [innerHTML]="barcode()"></div>
            <small>…{{ issued()!.visibleFragment }}</small>
          </div>
          <p class="form-hint">Emitido el {{ issued()!.issuedAtUtc | date:'dd/MM/yyyy HH:mm' }}. Este código no se podrá volver a ver.</p>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          @if (notice()) { <p role="status">{{ notice() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" (click)="close()">Cerrar</button>
            <button class="button button--secondary" type="button" (click)="download('jpg')">Descargar JPG</button>
            <button class="button button--secondary" type="button" (click)="download('pdf')">Descargar PDF</button>
            <button class="button button--primary" type="button" (click)="print()">Imprimir carné</button></div>
        }
      </div>
    </div>`,
})
export class UserCredential {
  private readonly api = inject(AdminApi);
  private readonly printer = inject(TicketPrinter);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly exporter = inject(CredentialCardExporter);
  readonly establishment = inject(EstablishmentService);
  readonly user = input.required<AdminUser>();
  readonly closed = output<void>();
  readonly issued = signal<IssuedCredential | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly roleName = computed(() => this.user().roles[0]?.name ?? 'Sin rol');
  /** The SVG is generated here from the token (digits only), so it is trusted; Angular would strip the <svg> otherwise. */
  readonly barcode = computed(() => { const value = this.issued(); return this.sanitizer.bypassSecurityTrustHtml(value ? code128Svg(value.credentialToken, 1.6, 70) : ''); });

  async issue(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try { this.issued.set(await this.api.issueCredential(this.user().id)); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.busy.set(false); }
  }

  private card(value: IssuedCredential): CredentialCard {
    return { businessName: this.establishment.name(), displayName: this.user().displayName, userName: this.user().userName, roleName: this.roleName(), token: value.credentialToken, fragment: value.visibleFragment };
  }

  /** Saves the card as an image or a PDF (card-sized page) so it can be printed anywhere. */
  async download(format: 'jpg' | 'pdf'): Promise<void> {
    const value = this.issued(); if (!value) return;
    this.error.set(''); this.notice.set('');
    try {
      const blob = format === 'pdf' ? await this.exporter.pdf(this.card(value)) : await this.exporter.jpeg(this.card(value));
      this.exporter.save(blob, `carne-${this.user().userName.replace(/[^A-Za-z0-9._-]/g, '_')}.${format}`);
      this.notice.set(format === 'pdf' ? 'PDF del carné descargado.' : 'Imagen del carné descargada.');
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'No se pudo generar el archivo.'); }
  }

  async print(): Promise<void> {
    const value = this.issued(); if (!value) return;
    this.error.set(''); this.notice.set('');
    try {
      await this.printer.print(buildCredentialCardHtml(this.card(value)));
      this.notice.set('Carné enviado a la impresión.');
    } catch { this.error.set('No se pudo abrir la impresión. Revisa la impresora e inténtalo de nuevo; el código sigue visible en esta ventana.'); }
  }

  /** Forgets the token as soon as the window closes. */
  close(): void { this.issued.set(null); this.closed.emit(); }
}
