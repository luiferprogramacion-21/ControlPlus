import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ChangePolicy, ConfigApi, LayawaySettings } from './config-api';

/** Time limit for product changes and the parameters of layaways. Both only affect documents created afterwards. */
@Component({
  selector: 'cp-settings-panel',
  imports: [FormsModule],
  template: `
    @if (loadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ loadError() }} <button class="link-button" type="button" (click)="load()">Reintentar</button></div> }

    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Cambios de productos</h3><p class="muted">Días que el cliente tiene para cambiar productos de una venta. Cada venta conserva el plazo con el que se registró.</p></div></div>
      @if (policy(); as current) {
        <form class="settings-form" (submit)="$event.preventDefault(); savePolicy()">
          <label class="field-label" for="change-days">Días límite para cambios</label>
          <input id="change-days" class="text-field" name="changeDays" type="number" min="1" step="1" [(ngModel)]="days">
          @if (policyError()) { <p class="field-error" role="alert">{{ policyError() }}</p> }
          @if (policyNotice()) { <p class="field-hint" role="status">{{ policyNotice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="savingPolicy() || Number(days) === current.days">{{ savingPolicy() ? 'Guardando…' : 'Guardar plazo' }}</button></div>
        </form>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>

    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Apartados</h3><p class="muted">Pago inicial mínimo y plazo para retirar los productos. Los apartados existentes conservan lo que tenían al crearse.</p></div></div>
      @if (layaway(); as current) {
        <form class="settings-form" (submit)="$event.preventDefault(); saveLayaway()">
          <label class="field-label" for="layaway-percentage">Porcentaje mínimo del pago inicial</label>
          <div class="money-field"><span>%</span><input id="layaway-percentage" name="layawayPercentage" type="number" min="0.01" max="100" step="0.01" [(ngModel)]="percentage"><b>del total</b></div>
          <label class="field-label field-label--spaced" for="layaway-days">Plazo por defecto</label>
          <div class="money-field"><span>#</span><input id="layaway-days" name="layawayDays" type="number" min="1" max="365" step="1" [(ngModel)]="layawayDays" [disabled]="chooseEachDate"><b>días</b></div>
          <label class="settings-check"><input type="checkbox" name="chooseEachDate" [(ngModel)]="chooseEachDate"> Elegir la fecha límite en cada apartado</label>
          @if (layawayError()) { <p class="field-error" role="alert">{{ layawayError() }}</p> }
          @if (layawayNotice()) { <p class="field-hint" role="status">{{ layawayNotice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="savingLayaway() || !layawayChanged(current)">{{ savingLayaway() ? 'Guardando…' : 'Guardar apartados' }}</button></div>
        </form>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>`,
  styles: ['.settings-form { max-width: 460px; } .settings-check { display: flex; align-items: center; gap: 8px; margin-top: 10px; font-size: 13px; }'],
})
export class SettingsPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly Number = Number;
  readonly policy = signal<ChangePolicy | null>(null);
  readonly layaway = signal<LayawaySettings | null>(null);
  readonly loadError = signal('');
  readonly savingPolicy = signal(false);
  readonly savingLayaway = signal(false);
  readonly policyError = signal(''); readonly policyNotice = signal('');
  readonly layawayError = signal(''); readonly layawayNotice = signal('');
  days = 15;
  percentage = 20;
  layawayDays: number | null = 15;
  chooseEachDate = false;

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.loadError.set('');
    try {
      const [policy, layaway] = await Promise.all([this.api.changePolicy(), this.api.layawaySettings()]);
      this.applyPolicy(policy); this.applyLayaway(layaway);
    } catch (e) { this.loadError.set(errorText(e)); }
  }
  private applyPolicy(policy: ChangePolicy): void { this.policy.set(policy); this.days = policy.days; }
  private applyLayaway(settings: LayawaySettings): void {
    this.layaway.set(settings); this.percentage = settings.minimumPercentage;
    this.chooseEachDate = settings.defaultDays === null; this.layawayDays = settings.defaultDays ?? 15;
  }

  async savePolicy(): Promise<void> {
    const current = this.policy(); if (!current || this.savingPolicy()) return;
    const days = Number(this.days);
    if (!Number.isInteger(days) || days < 1) { this.policyError.set('Indica un número entero de días mayor que cero.'); return; }
    this.savingPolicy.set(true); this.policyError.set(''); this.policyNotice.set('');
    try { this.applyPolicy(await this.api.updateChangePolicy(days, current.version)); this.policyNotice.set('Plazo guardado. Aplica a las ventas nuevas.'); }
    catch (e) { this.policyError.set(errorText(e)); await this.refreshPolicy(); }
    finally { this.savingPolicy.set(false); }
  }
  private async refreshPolicy(): Promise<void> { try { this.policy.set(await this.api.changePolicy()); } catch { /* el error ya se muestra */ } }

  layawayChanged(current: LayawaySettings): boolean {
    const days = this.chooseEachDate ? null : Number(this.layawayDays);
    return Number(this.percentage) !== current.minimumPercentage || days !== current.defaultDays;
  }
  validateLayaway(): string {
    const percentage = Number(this.percentage);
    if (!Number.isFinite(percentage) || percentage <= 0 || percentage > 100) return 'El porcentaje debe ser mayor que cero y no superar 100.';
    if (Math.round(percentage * 100) !== percentage * 100) return 'El porcentaje admite hasta dos decimales.';
    if (!this.chooseEachDate) { const days = Number(this.layawayDays); if (!Number.isInteger(days) || days < 1 || days > 365) return 'El plazo debe ser un entero entre 1 y 365 días.'; }
    return '';
  }
  async saveLayaway(): Promise<void> {
    const current = this.layaway(); if (!current || this.savingLayaway()) return;
    const issue = this.validateLayaway(); if (issue) { this.layawayError.set(issue); return; }
    this.savingLayaway.set(true); this.layawayError.set(''); this.layawayNotice.set('');
    try {
      this.applyLayaway(await this.api.updateLayawaySettings(Number(this.percentage), this.chooseEachDate ? null : Number(this.layawayDays), current.version));
      this.layawayNotice.set('Parámetros guardados. Aplican a los apartados nuevos.');
    } catch (e) { this.layawayError.set(errorText(e)); try { this.layaway.set(await this.api.layawaySettings()); } catch { /* el error ya se muestra */ } }
    finally { this.savingLayaway.set(false); }
  }
}
