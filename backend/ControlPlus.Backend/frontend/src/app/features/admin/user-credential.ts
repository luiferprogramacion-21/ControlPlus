import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, input, OnDestroy, OnInit, output, signal } from '@angular/core';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { errorText } from '../../core/api';
import { code128DataUrl } from '../../core/code128';
import { EstablishmentService } from '../../core/establishment';
import { TicketPrinter } from '../sales/ticket';
import { AdminApi, AdminUser, IssuedCredential, OperatorCredential } from './admin-api';
import { buildCredentialCardHtml, CredentialCard } from './credential-card';
import { CredentialCardExporter } from './credential-card-export';
import { toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';

/**
 * Shows the current operator card to its Administrator; old hash-only cards need to be reissued once.
 * The displayed token stays only in memory and is forgotten when the window closes.
 */
@Component({
  selector: 'cp-user-credential',
  imports: [WindowSheet, DatePipe],
  styleUrl: './admin.scss',
  template: `
    <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="user-credential-title"><div class="module-form-sheet__backdrop" (click)="close()"></div>
      <div class="panel panel--padded module-form credential-sheet">
        <div class="panel-heading"><h2 id="user-credential-title">Usuario: {{ user().displayName }}</h2></div>
        <ng-content select="[userTabs]" />
        <p class="muted">{{ user().userName }} · {{ roleName() }}</p>
        @if (loading()) { <p role="status">Cargando carné…</p> }
        @if (!issued()) {
          <p>El carné lleva un código de barras que el lector escanea para iniciar una sesión rápida de caja o para que esta persona autorice descuentos, anulaciones y demás operaciones sin escribir usuario y contraseña.</p>
          @if (current(); as card) {
            <div class="credential-preview" aria-label="Datos del carné anterior"><strong>{{ establishment.name() }}</strong><p><b>{{ user().displayName }}</b><br>{{ roleName() }} · {{ user().userName }}</p><small>Carné {{ card.state === 'VENCIDA' ? 'vencido' : 'vigente' }} · …{{ card.visibleFragment }}</small></div>
            <p class="form-hint">Este carné se emitió con la versión anterior y su código no se puede recuperar. Para verlo e imprimirlo de nuevo, emite uno nuevo una sola vez.</p>
          } @else if (!loading()) { <p class="form-hint">Esta persona no tiene carné vigente.</p> }
          <p class="form-hint">Emitir uno nuevo <strong>revoca el anterior</strong>. Los nuevos carnés podrán consultarse aquí después.</p>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions">@if (current()) { <button class="button button--secondary" type="button" [disabled]="busy() || loading()" (click)="remove()">Eliminar carné</button> }
            <button class="button button--primary" type="button" [disabled]="busy() || loading() || !user().isActive" (click)="issue()">{{ busy() ? 'Emitiendo…' : 'Emitir carné' }}</button>
            <button class="button button--secondary" type="button" (click)="close()">Cerrar</button></div>
          @if (!user().isActive) { <p class="field-error">El usuario está inactivo y no puede recibir carné.</p> }
        } @else {
          <div class="credential-preview" aria-label="Vista previa del carné">
            <div class="credential-preview__head"><strong>{{ establishment.name() }}</strong><span>Carné de operador</span></div>
            <p><b>{{ user().displayName }}</b><br><small>{{ roleName() }} · {{ user().userName }}</small></p>
            <div class="credential-preview__barcode"><img [src]="barcode()" alt="Código de barras del carné"></div>
            <small>…{{ issued()!.visibleFragment }}</small>
          </div>
          <p class="form-hint">Emitido el {{ issued()!.issuedAtUtc | date:'dd/MM/yyyy HH:mm' }}. Puedes volver a consultarlo desde Usuarios → Carné.</p>
          @if (current()?.state === 'VENCIDA') { <p class="field-error">Este carné venció y ya no sirve para operar.</p> }
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" [disabled]="busy()" (click)="remove()">Eliminar carné</button>
            <button class="button button--secondary" type="button" (click)="download('jpg')">Descargar JPG</button>
            <button class="button button--secondary" type="button" (click)="download('pdf')">Descargar PDF</button>
            <button class="button button--primary" type="button" (click)="print()">Imprimir carné</button>
            <button class="button button--secondary" type="button" (click)="close()">Cerrar</button></div>
        }
      </div>
    </div>`,
})
export class UserCredential implements OnInit, OnDestroy {
  private readonly api = inject(AdminApi);
  private readonly printer = inject(TicketPrinter);
  private readonly exporter = inject(CredentialCardExporter);
  private readonly confirmation = inject(ConfirmationDialog);
  private readonly destroyRef = inject(DestroyRef);
  private confirming = false;
  private disposed = false;
  readonly establishment = inject(EstablishmentService);
  readonly user = input.required<AdminUser>();
  readonly closed = output<void>();
  readonly issued = signal<IssuedCredential | null>(null);
  readonly current = signal<OperatorCredential | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly roleName = computed(() => this.user().roles[0]?.name ?? 'Sin rol');
  /** El SVG generado se muestra como imagen autocontenida, sin insertar HTML en el DOM. */
  readonly barcode = computed(() => { const value = this.issued(); return value ? code128DataUrl(value.credentialToken, 1.6, 70) : ''; });

  ngOnInit(): void { void this.loadCurrentCredential(); }

  private async loadCurrentCredential(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const card = await this.api.currentCredential(this.user().id);
      if (this.disposed) return;
      this.current.set(card);
      if (card?.credentialToken) this.issued.set({ ...card, credentialToken: card.credentialToken });
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  async issue(): Promise<void> {
    if (this.disposed || this.busy() || this.loading() || this.confirming) return;
    if (this.current()) {
      this.confirming = true;
      let accepted = false;
      try { accepted = await this.confirmation.confirm({ title: '¿Emitir un carné nuevo?',
        message: `Vas a reemplazar el carné de ${this.user().displayName}. El carné anterior dejará de servir para entrar o autorizar operaciones. El reemplazo se realiza solo cuando se completa la emisión.`,
        confirmText: 'Emitir carné' }, this.destroyRef); }
      finally { this.confirming = false; }
      if (!accepted || this.disposed) return;
    }
    this.busy.set(true); this.error.set('');
    try {
      const card = await this.api.issueCredential(this.user().id);
      if (this.disposed) return;
      this.issued.set(card); this.current.set({ ...card, state: 'ACTIVA', version: 1 });
    }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.busy.set(false); }
  }

  async remove(): Promise<void> {
    const card = this.current();
    if (!card || this.disposed || this.busy() || this.confirming) return;
    this.confirming = true;
    let accepted = false;
    try { accepted = await this.confirmation.confirm({ title: '¿Eliminar este carné?',
      message: 'Dejará de servir para entrar o autorizar operaciones; el historial se conserva.',
      confirmText: 'Eliminar carné', danger: true }, this.destroyRef); }
    finally { this.confirming = false; }
    if (!accepted || this.disposed || this.busy() || this.current()?.credentialId !== card.credentialId) return;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    try {
      await this.api.revokeCredential(this.user().id, card.credentialId, card.version);
      this.issued.set(null); this.current.set(null); this.notice.set('Carné eliminado.');
    } catch (e) { this.error.set(errorText(e)); }
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
  close(): void { this.ngOnDestroy(); this.closed.emit(); }
  ngOnDestroy(): void {
    this.disposed = true;
    this.confirmation.cancel(this.destroyRef);
    this.issued.set(null); this.current.set(null);
  }
}
