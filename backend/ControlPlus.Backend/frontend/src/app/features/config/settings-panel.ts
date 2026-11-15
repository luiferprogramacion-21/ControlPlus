import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ChangePolicy, ConfigApi, CreditSettings, LayawaySettings, SalesSettings } from './config-api';

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
          <label class="settings-check"><input id="layaway-require-minimum" type="checkbox" role="switch" name="requireMinimum" [(ngModel)]="requireMinimum"> Exigir un pago inicial mínimo al crear el apartado</label>
          <p class="field-hint">{{ requireMinimum ? 'Quien crea el apartado debe cobrar al menos este porcentaje del total.' : 'Sin exigencia: el pago inicial puede ser cualquier valor mayor que cero.' }}</p>
          <label class="field-label field-label--spaced" for="layaway-percentage">Porcentaje mínimo del pago inicial</label>
          <div class="money-field"><span>%</span><input id="layaway-percentage" name="layawayPercentage" type="number" min="0.01" max="100" step="0.01" [(ngModel)]="percentage" [disabled]="!requireMinimum"><b>del total</b></div>
          <label class="field-label field-label--spaced" for="layaway-days">Plazo por defecto</label>
          <div class="money-field"><span>#</span><input id="layaway-days" name="layawayDays" type="number" min="1" max="365" step="1" [(ngModel)]="layawayDays" [disabled]="chooseEachDate"><b>días</b></div>
          <label class="settings-check"><input type="checkbox" name="chooseEachDate" [(ngModel)]="chooseEachDate"> Elegir la fecha límite en cada apartado</label>
          @if (layawayError()) { <p class="field-error" role="alert">{{ layawayError() }}</p> }
          @if (layawayNotice()) { <p class="field-hint" role="status">{{ layawayNotice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="savingLayaway() || !layawayChanged(current)">{{ savingLayaway() ? 'Guardando…' : 'Guardar apartados' }}</button></div>
        </form>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>

    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Créditos</h3><p class="muted">Plazo predeterminado del vencimiento al vender a crédito. Quien vende puede cambiar la fecha entre mañana y 90 días. Los créditos existentes conservan su vencimiento.</p></div></div>
      @if (credit(); as current) {
        <form class="settings-form" (submit)="$event.preventDefault(); saveCredit()">
          <label class="field-label" for="credit-days">Plazo predeterminado</label>
          <div class="money-field"><span>#</span><input id="credit-days" name="creditDays" type="number" min="1" max="90" step="1" [(ngModel)]="creditDays" [disabled]="creditChooseEachTime"><b>días</b></div>
          <label class="settings-check"><input type="checkbox" name="creditChooseEachTime" [(ngModel)]="creditChooseEachTime"> Elegir el vencimiento en cada crédito (sin plazo predeterminado)</label>
          @if (creditError()) { <p class="field-error" role="alert">{{ creditError() }}</p> }
          @if (creditNotice()) { <p class="field-hint" role="status">{{ creditNotice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="savingCredit() || !creditChanged(current)">{{ savingCredit() ? 'Guardando…' : 'Guardar créditos' }}</button></div>
        </form>
      } @else if (creditLoadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ creditLoadError() }} <button class="link-button" type="button" (click)="loadCredit()">Reintentar</button></div>
      } @else { <p class="muted" role="status">Cargando…</p> }
    </section>

    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Precio mayorista</h3><p class="muted">Unidades de un mismo producto desde las que se puede vender, apartar o entregar en un cambio a precio mayorista. Los documentos ya registrados no cambian.</p></div></div>
      @if (sales(); as current) {
        <form class="settings-form" (submit)="$event.preventDefault(); saveSales()">
          <label class="field-label" for="wholesale-minimum">Mínimo de unidades</label>
          <div class="money-field"><span>#</span><input id="wholesale-minimum" name="wholesaleMinimum" type="number" min="1" max="999" step="1" [(ngModel)]="wholesaleMinimum"><b>unidades</b></div>
          @if (salesError()) { <p class="field-error" role="alert">{{ salesError() }}</p> }
          @if (salesNotice()) { <p class="field-hint" role="status">{{ salesNotice() }}</p> }
          <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="savingSales() || Number(wholesaleMinimum) === current.wholesaleMinimumQuantity">{{ savingSales() ? 'Guardando…' : 'Guardar mínimo' }}</button></div>
        </form>
      } @else if (salesLoadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ salesLoadError() }} <button class="link-button" type="button" (click)="loadSales()">Reintentar</button></div>
      } @else { <p class="muted" role="status">Cargando…</p> }
    </section>`,
  styles: ['.settings-form { max-width: 460px; } .settings-check { display: flex; align-items: center; gap: 8px; margin-top: 10px; font-size: 13px; }'],
})
export class SettingsPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly Number = Number;
  readonly policy = signal<ChangePolicy | null>(null);
  readonly layaway = signal<LayawaySettings | null>(null);
  readonly loadError = signal('');
  readonly credit = signal<CreditSettings | null>(null);
  readonly creditLoadError = signal('');
  readonly savingCredit = signal(false);
  readonly creditError = signal(''); readonly creditNotice = signal('');
  creditDays: number | null = 30;
  creditChooseEachTime = false;
  readonly savingPolicy = signal(false);
  readonly savingLayaway = signal(false);
  readonly policyError = signal(''); readonly policyNotice = signal('');
  readonly layawayError = signal(''); readonly layawayNotice = signal('');
  days = 15;
  percentage = 20;
  requireMinimum = true;
  layawayDays: number | null = 15;
  chooseEachDate = false;

  readonly sales = signal<SalesSettings | null>(null);
  readonly salesLoadError = signal('');
  readonly savingSales = signal(false);
  readonly salesError = signal(''); readonly salesNotice = signal('');
  wholesaleMinimum: number | null = 12;
  ngOnInit(): void { void this.load(); void this.loadCredit(); void this.loadSales(); }
  async loadSales(): Promise<void> {
    this.salesLoadError.set('');
    try { this.applySales(await this.api.salesSettings()); } catch (e) { this.salesLoadError.set(errorText(e)); }
  }
  private applySales(settings: SalesSettings): void { this.sales.set(settings); this.wholesaleMinimum = settings.wholesaleMinimumQuantity; }
  validateSales(): string {
    const value = Number(this.wholesaleMinimum);
    return Number.isInteger(value) && value >= 1 && value <= 999 ? '' : 'El mínimo debe ser un número entero entre 1 y 999.';
  }
  async saveSales(): Promise<void> {
    const current = this.sales(); if (!current || this.savingSales()) return;
    const issue = this.validateSales(); if (issue) { this.salesError.set(issue); return; }
    this.savingSales.set(true); this.salesError.set(''); this.salesNotice.set('');
    try {
      this.applySales(await this.api.updateSalesSettings(Number(this.wholesaleMinimum), current.version));
      this.salesNotice.set('Mínimo guardado. Aplica a las ventas, apartados y cambios nuevos.');
    } catch (e) { this.salesError.set(errorText(e)); try { this.sales.set(await this.api.salesSettings()); } catch { /* el error ya se muestra */ } }
    finally { this.savingSales.set(false); }
  }

  async load(): Promise<void> {
    this.loadError.set('');
    try {
      const [policy, layaway] = await Promise.all([this.api.changePolicy(), this.api.layawaySettings()]);
      this.applyPolicy(policy); this.applyLayaway(layaway);
    } catch (e) { this.loadError.set(errorText(e)); }
  }
  /** Loaded on its own so that the rest of the panel keeps working if this setting cannot be read. */
  async loadCredit(): Promise<void> {
    this.creditLoadError.set('');
    try { this.applyCredit(await this.api.creditSettings()); } catch (e) { this.creditLoadError.set(errorText(e)); }
  }
  private applyCredit(settings: CreditSettings): void {
    this.credit.set(settings); this.creditChooseEachTime = settings.defaultDays === null; this.creditDays = settings.defaultDays ?? 30;
  }
  creditChanged(current: CreditSettings): boolean { return (this.creditChooseEachTime ? null : Number(this.creditDays)) !== current.defaultDays; }
  validateCredit(): string {
    if (this.creditChooseEachTime) return '';
    const days = Number(this.creditDays);
    return Number.isInteger(days) && days >= 1 && days <= 90 ? '' : 'El plazo debe ser un entero entre 1 y 90 días.';
  }
  async saveCredit(): Promise<void> {
    const current = this.credit(); if (!current || this.savingCredit()) return;
    const issue = this.validateCredit(); if (issue) { this.creditError.set(issue); return; }
    this.savingCredit.set(true); this.creditError.set(''); this.creditNotice.set('');
    try {
      this.applyCredit(await this.api.updateCreditSettings(this.creditChooseEachTime ? null : Number(this.creditDays), current.version));
      this.creditNotice.set('Plazo guardado. Aplica a los créditos nuevos.');
    } catch (e) { this.creditError.set(errorText(e)); try { this.credit.set(await this.api.creditSettings()); } catch { /* el error ya se muestra */ } }
    finally { this.savingCredit.set(false); }
  }

  private applyPolicy(policy: ChangePolicy): void { this.policy.set(policy); this.days = policy.days; }
  private applyLayaway(settings: LayawaySettings): void {
    this.layaway.set(settings); this.percentage = settings.minimumPercentage; this.requireMinimum = settings.requireMinimumInitial;
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
    return Number(this.percentage) !== current.minimumPercentage || days !== current.defaultDays || this.requireMinimum !== current.requireMinimumInitial;
  }
  validateLayaway(): string {
    const percentage = Number(this.percentage);
    // The percentage is kept even when the minimum is not required, so it must stay valid.
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
      this.applyLayaway(await this.api.updateLayawaySettings(Number(this.percentage), this.chooseEachDate ? null : Number(this.layawayDays), this.requireMinimum, current.version));
      this.layawayNotice.set('Parámetros guardados. Aplican a los apartados nuevos.');
    } catch (e) { this.layawayError.set(errorText(e)); try { this.layaway.set(await this.api.layawaySettings()); } catch { /* el error ya se muestra */ } }
    finally { this.savingLayaway.set(false); }
  }
}
