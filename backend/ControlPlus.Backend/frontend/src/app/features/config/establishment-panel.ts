import { InputFormat } from '../../shared/input-format';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { Establishment, EstablishmentService } from '../../core/establishment';
import { toastSignal } from '../../core/toast';

/** General business data from mock-up 21. Email is intentionally absent because V1 has no persisted column. */
@Component({
  selector: 'cp-establishment-panel',
  imports: [FormsModule, InputFormat],
  template: `
    <section class="panel module-panel establishment-panel">
      <div class="module-toolbar"><div><h3>Datos del establecimiento</h3><p class="muted">Información que identifica el negocio en ControlPlus y en los comprobantes.</p></div></div>
      @if (loadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ loadError() }} <button class="link-button" type="button" (click)="load()">Reintentar</button></div> }
      @if (current(); as saved) {
        <form class="establishment-form" (submit)="$event.preventDefault(); save()">
          <div class="form-grid form-grid--two">
            <label class="field-label" for="business-name">Nombre comercial<input id="business-name" class="text-field" name="businessName" maxlength="200" [(ngModel)]="commercialName" autocomplete="organization"></label>
            <label class="field-label" for="business-nit">NIT o documento <span>(opcional)</span><input id="business-nit" class="text-field" name="businessNit" cpFormat="document" cpDocumentType="NIT" [(ngModel)]="identification"></label>
            <label class="field-label" for="business-phone">Teléfono <span>(opcional)</span><input id="business-phone" class="text-field" name="businessPhone" cpFormat="phone" [(ngModel)]="phone"></label>
            <label class="field-label" for="business-address">Dirección <span>(opcional)</span><input id="business-address" class="text-field" name="businessAddress" maxlength="300" [(ngModel)]="address" autocomplete="street-address"></label>
          </div>
          <div class="receipt-copy-grid">
            <label class="field-label" for="receipt-header">Encabezado del comprobante <span>(opcional)</span><textarea id="receipt-header" class="text-field textarea" name="receiptHeader" rows="3" maxlength="1000" [(ngModel)]="receiptHeader" placeholder="Mensaje que aparece antes del nombre del negocio"></textarea></label>
            <label class="field-label" for="receipt-footer">Pie del comprobante <span>(opcional)</span><textarea id="receipt-footer" class="text-field textarea" name="receiptFooter" rows="3" maxlength="1000" [(ngModel)]="receiptFooter" placeholder="Ej. Gracias por su compra"></textarea></label>
          </div>
          <p class="field-hint">Estos datos se usan en tickets, carnés, pedidos y pantallas operativas. No se solicita correo porque no forma parte del establecimiento en V1.</p>
          @if (validation()) { <p class="field-error" role="alert">{{ validation() }}</p> }
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="saving() || !changed(saved)">{{ saving() ? 'Guardando…' : 'Guardar datos' }}</button></div>
        </form>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>`,
  styles: [`
    .establishment-form { max-width: 820px; }
    .field-label { display: grid; gap: 6px; }
    .field-label span { color: #718096; font-weight: 400; }
    .receipt-copy-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; margin-top: 18px; }
    .textarea { resize: vertical; min-height: 86px; }
    @container cp (max-width: 720px) { .receipt-copy-grid { grid-template-columns: 1fr; } }
  `],
})
export class EstablishmentPanel implements OnInit {
  private readonly establishment = inject(EstablishmentService);
  readonly current = signal<Establishment | null>(null);
  readonly loadError = toastSignal('error');
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  commercialName = '';
  identification = '';
  phone = '';
  address = '';
  receiptHeader = '';
  receiptFooter = '';

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.loadError.set('');
    try { this.apply(await this.establishment.load(true)); }
    catch (error) { this.loadError.set(errorText(error)); }
  }

  validation(): string {
    if (!this.commercialName.trim()) return 'El nombre comercial es obligatorio.';
    return '';
  }

  changed(saved: Establishment): boolean {
    return this.commercialName.trim() !== saved.commercialName ||
      this.optional(this.identification) !== saved.identification ||
      this.optional(this.phone) !== saved.phone || this.optional(this.address) !== saved.address ||
      this.optional(this.receiptHeader) !== saved.receiptHeader ||
      this.optional(this.receiptFooter) !== saved.receiptFooter;
  }

  async save(): Promise<void> {
    const saved = this.current();
    if (!saved || this.saving()) return;
    const issue = this.validation();
    if (issue) { this.error.set(issue); return; }
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try {
      this.apply(await this.establishment.update({
        commercialName: this.commercialName.trim(), identification: this.optional(this.identification),
        phone: this.optional(this.phone), address: this.optional(this.address),
        receiptHeader: this.optional(this.receiptHeader), receiptFooter: this.optional(this.receiptFooter),
        expectedVersion: saved.version,
      }));
      this.notice.set('Datos guardados. El nombre y los comprobantes ya usan esta información.');
    } catch (error) {
      this.error.set(errorText(error));
      try { this.apply(await this.establishment.load(true)); } catch { /* se conserva el error original */ }
    } finally { this.saving.set(false); }
  }

  private apply(value: Establishment): void {
    this.current.set(value); this.commercialName = value.commercialName; this.identification = value.identification ?? '';
    this.phone = value.phone ?? ''; this.address = value.address ?? '';
    this.receiptHeader = value.receiptHeader ?? ''; this.receiptFooter = value.receiptFooter ?? '';
  }

  private optional(value: string): string | null { return value.trim() || null; }
}
