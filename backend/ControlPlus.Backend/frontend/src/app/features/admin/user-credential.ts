import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, OnDestroy, OnInit, output, signal } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { errorText } from '../../core/api';
import { code128Svg } from '../../core/code128';
import { EstablishmentService } from '../../core/establishment';
import { TicketPrinter } from '../sales/ticket';
import { AdminApi, AdminUser, IssuedCredential, OperatorCredential } from './admin-api';
import { buildCredentialCardHtml, CredentialCard } from './credential-card';
import { CredentialCardExporter } from './credential-card-export';
import { toastSignal } from '../../core/toast';

@Component({
  selector: 'cp-credential-replacement-dialog',
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title>¿Emitir un carné nuevo?</h2>
    <mat-dialog-content id="credential-replacement-description">
      <p>Vas a reemplazar el carné de <strong>{{ data.displayName }}</strong>.</p>
      <p class="replacement-warning">El carné anterior dejará de servir para entrar o autorizar operaciones.</p>
      <p>El reemplazo se realiza solo cuando se completa la emisión.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button id="cancel-credential-replacement" class="button button--secondary" type="button" [mat-dialog-close]="false">Cancelar</button>
      <button class="button button--primary" type="button" [mat-dialog-close]="true">Emitir carné</button>
    </mat-dialog-actions>`,
  styles: [`
    mat-dialog-content p { margin:0 0 16px; line-height:1.6; }
    mat-dialog-content p:last-child { margin-bottom:0; }
    .replacement-warning { padding:12px 16px; border-left:3px solid var(--cp-warning); border-radius:4px; background:var(--cp-warning-soft); color:var(--cp-warning); }
    mat-dialog-actions { gap:8px; padding:16px 24px 24px; }
  `],
})
export class CredentialReplacementDialog {
  readonly data = inject<{ displayName: string }>(MAT_DIALOG_DATA);
}

/**
 * Shows the current operator card to its Administrator; old hash-only cards need to be reissued once.
 * The displayed token stays only in memory and is forgotten when the window closes.
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
        @if (loading()) { <p role="status">Cargando carné…</p> }
        @if (!issued()) {
          <p>El carné lleva un código de barras que el lector escanea para iniciar una sesión rápida de caja o para que esta persona autorice descuentos, anulaciones y demás operaciones sin escribir usuario y contraseña.</p>
          @if (current(); as card) {
            <div class="credential-preview" aria-label="Datos del carné anterior"><strong>{{ establishment.name() }}</strong><p><b>{{ user().displayName }}</b><br>{{ roleName() }} · {{ user().userName }}</p><small>Carné {{ card.state === 'VENCIDA' ? 'vencido' : 'vigente' }} · …{{ card.visibleFragment }}</small></div>
            <p class="form-hint">Este carné se emitió con la versión anterior y su código no se puede recuperar. Para verlo e imprimirlo de nuevo, emite uno nuevo una sola vez.</p>
          } @else if (!loading()) { <p class="form-hint">Esta persona no tiene carné vigente.</p> }
          <p class="form-hint">Emitir uno nuevo <strong>revoca el anterior</strong>. Los nuevos carnés podrán consultarse aquí después.</p>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" (click)="close()">Cerrar</button>
            @if (current()) { <button class="button button--secondary" type="button" [disabled]="busy() || loading()" (click)="remove()">Eliminar carné</button> }
            <button class="button button--primary" type="button" [disabled]="busy() || loading() || !user().isActive" (click)="issue()">{{ busy() ? 'Emitiendo…' : 'Emitir carné' }}</button></div>
          @if (!user().isActive) { <p class="field-error">El usuario está inactivo y no puede recibir carné.</p> }
        } @else {
          <div class="credential-preview" aria-label="Vista previa del carné">
            <div class="credential-preview__head"><strong>{{ establishment.name() }}</strong><span>Credencial de operador</span></div>
            <p><b>{{ user().displayName }}</b><br><small>{{ roleName() }} · {{ user().userName }}</small></p>
            <div class="credential-preview__barcode" [innerHTML]="barcode()"></div>
            <small>…{{ issued()!.visibleFragment }}</small>
          </div>
          <p class="form-hint">Emitido el {{ issued()!.issuedAtUtc | date:'dd/MM/yyyy HH:mm' }}. Puedes volver a consultarlo desde Usuarios → Carné.</p>
          @if (current()?.state === 'VENCIDA') { <p class="field-error">Este carné venció y ya no sirve para operar.</p> }
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" (click)="close()">Cerrar</button>
            <button class="button button--secondary" type="button" [disabled]="busy()" (click)="remove()">Eliminar carné</button>
            <button class="button button--secondary" type="button" (click)="download('jpg')">Descargar JPG</button>
            <button class="button button--secondary" type="button" (click)="download('pdf')">Descargar PDF</button>
            <button class="button button--primary" type="button" (click)="print()">Imprimir carné</button></div>
        }
      </div>
    </div>`,
})
export class UserCredential implements OnInit, OnDestroy {
  private readonly api = inject(AdminApi);
  private readonly printer = inject(TicketPrinter);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly exporter = inject(CredentialCardExporter);
  private readonly dialog = inject(MatDialog);
  private replacementDialog: MatDialogRef<CredentialReplacementDialog, boolean> | null = null;
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
  /** The SVG is generated here from the token (digits only), so it is trusted; Angular would strip the <svg> otherwise. */
  readonly barcode = computed(() => { const value = this.issued(); return this.sanitizer.bypassSecurityTrustHtml(value ? code128Svg(value.credentialToken, 1.6, 70) : ''); });

  async ngOnInit(): Promise<void> {
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
    if (this.disposed || this.busy() || this.loading() || this.replacementDialog) return;
    if (this.current()) {
      const ref = this.dialog.open<CredentialReplacementDialog, { displayName: string }, boolean>(CredentialReplacementDialog, {
        data: { displayName: this.user().displayName }, width: '460px', maxWidth: 'calc(100vw - 32px)',
        role: 'alertdialog', ariaDescribedBy: 'credential-replacement-description',
        autoFocus: '#cancel-credential-replacement', restoreFocus: true,
      });
      this.replacementDialog = ref;
      let accepted = false;
      try { accepted = await firstValueFrom(ref.afterClosed(), { defaultValue: false }) === true; }
      finally { if (this.replacementDialog === ref) this.replacementDialog = null; }
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
    if (!card || this.busy() || this.replacementDialog || !confirm('¿Eliminar este carné? Dejará de servir para entrar o autorizar operaciones; el historial se conserva.')) return;
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
    this.replacementDialog?.close(false); this.replacementDialog = null;
    this.issued.set(null); this.current.set(null);
  }
}
