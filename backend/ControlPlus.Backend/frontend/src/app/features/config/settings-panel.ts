import { NumberInput } from '../../shared/number-input';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ChangePolicy, ConfigApi, CreditSettings, LayawaySettings, SalesSettings } from './config-api';
import { toastSignal } from '../../core/toast';
import { HelpTip } from '../../shared/help-tip';

/** Reglas de cambios, apartados, créditos y mayorista, en tarjetas (rediseño aprobado el 19 de noviembre de 2026). Solo afectan lo que se registre después. */
@Component({
  selector: 'cp-settings-panel',
  imports: [HelpTip, NumberInput, FormsModule],
  template: `
    @if (loadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ loadError() }} <button class="link-button" type="button" (click)="load()">Reintentar</button></div> }

    <div class="rules-grid">
      <section class="rule-card" aria-labelledby="rule-change-title">
        <header class="rule-card__head"><span class="rule-card__icon rule-card__icon--blue" aria-hidden="true">⇄</span>
          <div><h3 id="rule-change-title">Cambios de productos</h3><p>Tiempo que tiene el cliente para cambiar lo que compró.</p></div></header>
        @if (policy(); as current) {
          <form class="rule-card__body" (submit)="$event.preventDefault(); savePolicy()">
            <label class="rule-field"><span class="rule-field__label">Plazo para cambios<cp-help text="Días que tiene el cliente, desde la compra, para cambiar un producto." /></span>
              <span class="rule-input"><input id="change-days" name="changeDays" type="text" cpNumberInput min="1" step="1" [(ngModel)]="days"><b>días</b></span></label>
            <p class="rule-note">Cada venta conserva el plazo con el que se registró.</p>
            @if (policyError()) { <p class="field-error" role="alert">{{ policyError() }}</p> }
            <div class="rule-card__foot"><button class="button button--primary" type="submit" [disabled]="savingPolicy() || Number(days) === current.days">{{ savingPolicy() ? 'Guardando…' : 'Guardar' }}</button></div>
          </form>
        } @else if (!loadError()) { <p class="muted rule-card__loading" role="status">Cargando…</p> }
      </section>

      <section class="rule-card" aria-labelledby="rule-layaway-title">
        <header class="rule-card__head"><span class="rule-card__icon rule-card__icon--orange" aria-hidden="true">◷</span>
          <div><h3 id="rule-layaway-title">Apartados</h3><p>Pago inicial y tiempo para retirar los productos.</p></div></header>
        @if (layaway(); as current) {
          <form class="rule-card__body" (submit)="$event.preventDefault(); saveLayaway()">
            <label class="rule-switch"><input id="layaway-require-minimum" type="checkbox" role="switch" name="requireMinimum" [(ngModel)]="requireMinimum"><span class="rule-switch__track" aria-hidden="true"></span><span>Exigir un pago inicial mínimo</span></label>
            @if (requireMinimum) {
              <label class="rule-field rule-field--nested"><span class="rule-field__label">Pago inicial mínimo<cp-help text="Porcentaje del total que el cliente debe pagar al apartar." /></span>
                <span class="rule-input"><input id="layaway-percentage" name="layawayPercentage" type="text" cpNumberInput min="0.01" max="100" step="0.01" [(ngModel)]="percentage"><b>% del total</b></span></label>
            } @else { <p class="rule-note rule-note--nested">El pago inicial puede ser cualquier valor desde $1.</p> }
            <div class="rule-divider"></div>
            <label class="rule-switch"><input type="checkbox" role="switch" name="chooseEachDate" [(ngModel)]="chooseEachDate"><span class="rule-switch__track" aria-hidden="true"></span><span>Elegir la fecha límite en cada apartado</span></label>
            @if (!chooseEachDate) {
              <label class="rule-field rule-field--nested"><span class="rule-field__label">Plazo para retirar</span>
                <span class="rule-input"><input id="layaway-days" name="layawayDays" type="text" cpNumberInput min="1" max="365" step="1" [(ngModel)]="layawayDays"><b>días</b></span></label>
            }
            <p class="rule-note">Los apartados existentes conservan lo que tenían al crearse.</p>
            @if (layawayError()) { <p class="field-error" role="alert">{{ layawayError() }}</p> }
            <div class="rule-card__foot"><button class="button button--primary" type="submit" [disabled]="savingLayaway() || !layawayChanged(current)">{{ savingLayaway() ? 'Guardando…' : 'Guardar' }}</button></div>
          </form>
        } @else if (!loadError()) { <p class="muted rule-card__loading" role="status">Cargando…</p> }
      </section>

      <section class="rule-card" aria-labelledby="rule-credit-title">
        <header class="rule-card__head"><span class="rule-card__icon rule-card__icon--green" aria-hidden="true">$</span>
          <div><h3 id="rule-credit-title">Créditos</h3><p>Vencimiento sugerido y control de clientes morosos.</p></div></header>
        @if (credit(); as current) {
          <form class="rule-card__body" (submit)="$event.preventDefault(); saveCredit()">
            <label class="rule-switch"><input type="checkbox" role="switch" name="creditChooseEachTime" [(ngModel)]="creditChooseEachTime"><span class="rule-switch__track" aria-hidden="true"></span><span>Elegir el vencimiento en cada venta</span></label>
            @if (!creditChooseEachTime) {
              <div class="rule-field rule-field--nested"><span class="rule-field__label">Vencimiento sugerido</span>
                <span class="rule-term-controls">
                  @if (creditUnit === 'days') { <span class="rule-input"><input id="credit-days" name="creditDays" type="text" cpNumberInput min="1" max="180" step="1" [(ngModel)]="creditDays" aria-label="Cantidad de días"><b>días</b></span> }
                  @else { <span class="rule-input"><input id="credit-months" name="creditMonths" type="text" cpNumberInput min="1" max="6" step="1" [(ngModel)]="creditMonths" aria-label="Cantidad de meses"><b>meses</b></span> }
                  <select id="credit-unit" class="rule-unit" name="creditUnit" [(ngModel)]="creditUnit" aria-label="Unidad del plazo de crédito"><option value="days">Días</option><option value="months">Meses</option></select>
                </span></div>
            }
            <p class="rule-note">Quien vende puede cambiar la fecha entre mañana y {{ creditUnit === 'months' && !creditChooseEachTime ? '6 meses calendario' : '180 días' }}. El plazo se cuenta desde la fecha de la venta.</p>
            <div class="rule-divider"></div>
            <label class="rule-switch"><input type="checkbox" role="switch" name="creditBlock" [(ngModel)]="creditBlock"><span class="rule-switch__track" aria-hidden="true"></span><span>Bloquear créditos a morosos o por encima del cupo</span></label>
            <p class="rule-note rule-note--nested">{{ creditBlock ? 'Solo el Administrador puede autorizar esas ventas.' : 'Se avisa, pero se puede vender.' }}</p>
            @if (creditError()) { <p class="field-error" role="alert">{{ creditError() }}</p> }
            <div class="rule-card__foot"><button class="button button--primary" type="submit" [disabled]="savingCredit() || !creditChanged(current)">{{ savingCredit() ? 'Guardando…' : 'Guardar' }}</button></div>
          </form>
        } @else if (creditLoadError()) { <div class="inline-feedback inline-feedback--error rule-card__loading" role="alert">{{ creditLoadError() }} <button class="link-button" type="button" (click)="loadCredit()">Reintentar</button></div>
        } @else { <p class="muted rule-card__loading" role="status">Cargando…</p> }
      </section>

      <section class="rule-card" aria-labelledby="rule-wholesale-title">
        <header class="rule-card__head"><span class="rule-card__icon rule-card__icon--navy" aria-hidden="true">≡</span>
          <div><h3 id="rule-wholesale-title">Precio mayorista</h3><p>Desde cuántas unidades de un mismo producto aplica.</p></div></header>
        @if (sales(); as current) {
          <form class="rule-card__body" (submit)="$event.preventDefault(); saveSales()">
            <label class="rule-field"><span class="rule-field__label">Mínimo para mayorista<cp-help text="Unidades de un mismo producto que debe llevar el cliente para cobrarle el precio mayorista." /></span>
              <span class="rule-input"><input id="wholesale-minimum" name="wholesaleMinimum" type="text" cpNumberInput min="1" max="999" step="1" [(ngModel)]="wholesaleMinimum"><b>unidades</b></span></label>
            <p class="rule-note">Aplica en ventas, apartados y cambios. Lo ya registrado no cambia.</p>
            @if (salesError()) { <p class="field-error" role="alert">{{ salesError() }}</p> }
            <div class="rule-card__foot"><button class="button button--primary" type="submit" [disabled]="savingSales() || Number(wholesaleMinimum) === current.wholesaleMinimumQuantity">{{ savingSales() ? 'Guardando…' : 'Guardar' }}</button></div>
          </form>
        } @else if (salesLoadError()) { <div class="inline-feedback inline-feedback--error rule-card__loading" role="alert">{{ salesLoadError() }} <button class="link-button" type="button" (click)="loadSales()">Reintentar</button></div>
        } @else { <p class="muted rule-card__loading" role="status">Cargando…</p> }
      </section>
    </div>`,
  styleUrl: './settings-panel.scss',
})
export class SettingsPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly Number = Number;
  readonly policy = signal<ChangePolicy | null>(null);
  readonly layaway = signal<LayawaySettings | null>(null);
  readonly loadError = toastSignal('error');
  readonly credit = signal<CreditSettings | null>(null);
  readonly creditLoadError = toastSignal('error');
  readonly savingCredit = signal(false);
  readonly creditError = toastSignal('error'); readonly creditNotice = toastSignal('success');
  creditDays: number | null = 30;
  creditMonths: number | null = 3;
  creditUnit: 'days' | 'months' = 'days';
  creditChooseEachTime = false;
  creditBlock = false;
  readonly savingPolicy = signal(false);
  readonly savingLayaway = signal(false);
  readonly policyError = toastSignal('error'); readonly policyNotice = toastSignal('success');
  readonly layawayError = toastSignal('error'); readonly layawayNotice = toastSignal('success');
  days = 15;
  percentage = 20;
  requireMinimum = true;
  layawayDays: number | null = 15;
  chooseEachDate = false;

  readonly sales = signal<SalesSettings | null>(null);
  readonly salesLoadError = toastSignal('error');
  readonly savingSales = signal(false);
  readonly salesError = toastSignal('error'); readonly salesNotice = toastSignal('success');
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
    this.credit.set(settings);
    this.creditChooseEachTime = settings.defaultDays == null && settings.defaultMonths == null;
    this.creditUnit = settings.defaultMonths != null ? 'months' : 'days';
    this.creditDays = settings.defaultDays ?? 30;
    this.creditMonths = settings.defaultMonths ?? 3;
    this.creditBlock = settings.blockRiskyCredit === true;
  }
  creditChanged(current: CreditSettings): boolean {
    const days = this.creditChooseEachTime || this.creditUnit === 'months' ? null : Number(this.creditDays);
    const months = this.creditChooseEachTime || this.creditUnit === 'days' ? null : Number(this.creditMonths);
    return days !== current.defaultDays || months !== (current.defaultMonths ?? null) || this.creditBlock !== (current.blockRiskyCredit === true);
  }
  validateCredit(): string {
    if (this.creditChooseEachTime) return '';
    if (this.creditUnit === 'months') {
      const months = Number(this.creditMonths);
      return Number.isInteger(months) && months >= 1 && months <= 6 ? '' : 'El plazo debe ser un entero entre 1 y 6 meses.';
    }
    const days = Number(this.creditDays);
    return Number.isInteger(days) && days >= 1 && days <= 180 ? '' : 'El plazo debe ser un entero entre 1 y 180 días.';
  }
  async saveCredit(): Promise<void> {
    const current = this.credit(); if (!current || this.savingCredit()) return;
    const issue = this.validateCredit(); if (issue) { this.creditError.set(issue); return; }
    this.savingCredit.set(true); this.creditError.set(''); this.creditNotice.set('');
    try {
      const days = this.creditChooseEachTime || this.creditUnit === 'months' ? null : Number(this.creditDays);
      const months = this.creditChooseEachTime || this.creditUnit === 'days' ? null : Number(this.creditMonths);
      this.applyCredit(await this.api.updateCreditSettings(days, current.version, this.creditBlock, months));
      this.creditNotice.set('Guardado. Aplica a los créditos nuevos.');
    } catch (e) { this.creditError.set(errorText(e)); try { this.applyCredit(await this.api.creditSettings()); } catch { /* el error ya se muestra */ } }
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
