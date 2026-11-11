import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ConfigApi, VatSettings } from './config-api';

/** Validates the percentage typed by the Administrator. Empty text means valid. */
export function validateVatPercentage(enabled: boolean, value: unknown): string {
  const text = String(value ?? '').trim();
  if (!text) return enabled ? 'Indica el porcentaje de IVA para activarlo.' : '';
  const percentage = Number(text);
  if (!Number.isFinite(percentage) || percentage <= 0 || percentage > 100) return 'El porcentaje debe ser mayor que cero y no superar 100.';
  if (Math.round(percentage * 100) !== percentage * 100) return 'El porcentaje admite hasta dos decimales.';
  return '';
}

/** One VAT switch and percentage for the whole business. Only the Administrator reaches this panel; the server enforces it. */
@Component({
  selector: 'cp-vat-panel',
  imports: [FormsModule],
  template: `
    @if (loadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ loadError() }} <button class="link-button" type="button" (click)="load()">Reintentar</button></div> }
    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>IVA del negocio</h3><p class="muted">Un solo porcentaje para todas las ventas. Solo el Administrador puede activarlo, desactivarlo o cambiarlo.</p></div></div>
      @if (current(); as settings) {
        <form class="vat-form" (submit)="$event.preventDefault(); save()">
          <label class="vat-switch"><input id="vat-enabled" type="checkbox" role="switch" name="enabled" [(ngModel)]="enabled" [attr.aria-checked]="enabled"> <span>Cobrar IVA en las ventas</span></label>
          <p class="muted vat-explain" role="status">
            @if (enabled) { Los precios ya incluyen el IVA. Cada venta nueva guarda cuánto es base y cuánto es IVA; el total a pagar no cambia ni se suma IVA encima. }
            @else { Los precios no llevan IVA y las ventas nuevas no lo desglosan. }
          </p>
          <label class="field-label field-label--spaced" for="vat-percentage">Porcentaje de IVA</label>
          <div class="money-field"><span>%</span><input id="vat-percentage" name="percentage" type="number" min="0.01" max="100" step="0.01" inputmode="decimal" [(ngModel)]="percentage" [attr.aria-invalid]="issue() ? 'true' : null"><b>del precio</b></div>
          <p class="muted vat-explain">El cambio aplica solo a las ventas nuevas; las ventas anteriores conservan el IVA con el que se registraron. Apartados y cambios de producto usan el IVA de la venta original.</p>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          @if (notice()) { <p class="field-hint" role="status">{{ notice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="saving() || !changed(settings) || !!issue()">{{ saving() ? 'Guardando…' : 'Guardar IVA' }}</button></div>
        </form>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>`,
  styles: ['.vat-form { max-width: 460px; } .vat-switch { display: flex; align-items: center; gap: 10px; font-weight: 600; } .vat-switch input { width: 20px; height: 20px; } .vat-explain { margin: 8px 0 0; font-size: 13px; }'],
})
export class VatPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly current = signal<VatSettings | null>(null);
  readonly loadError = signal('');
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  enabled = false;
  percentage: number | string | null = null;

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.loadError.set('');
    try { this.apply(await this.api.vatSettings()); } catch (e) { this.loadError.set(errorText(e)); }
  }
  private apply(settings: VatSettings): void { this.current.set(settings); this.enabled = settings.enabled; this.percentage = settings.percentage; }

  issue(): string { return validateVatPercentage(this.enabled, this.percentage); }
  private value(): number | null { const text = String(this.percentage ?? '').trim(); return text ? Number(text) : null; }
  changed(settings: VatSettings): boolean { return this.enabled !== settings.enabled || this.value() !== settings.percentage; }

  async save(): Promise<void> {
    const settings = this.current(); if (!settings || this.saving()) return;
    const issue = this.issue(); if (issue) { this.error.set(issue); return; }
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try {
      this.apply(await this.api.updateVatSettings(this.enabled, this.value(), settings.version));
      this.notice.set(this.enabled ? 'IVA activado. Aplica a las ventas nuevas.' : 'IVA desactivado. Aplica a las ventas nuevas.');
    } catch (e) { this.error.set(errorText(e)); try { this.current.set(await this.api.vatSettings()); } catch { /* el error ya se muestra */ } }
    finally { this.saving.set(false); }
  }
}
