import { Component, inject, OnInit, signal } from '@angular/core';
import { errorText } from '../../core/api';
import { CashApi, CashState } from '../cash/cash-api';
import { toastSignal } from '../../core/toast';
import { SessionService } from '../../core/session.service';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';

type Mode = 'INDIVIDUAL' | 'COMPARTIDO';

/**
 * Modo de trabajo del turno (maqueta 21b, decisión del 18 de noviembre de 2026). Individual: una persona abre, vende y
 * cierra. Compartido: varios operadores se turnan en la caja y cada venta se desbloquea con su carné. Solo se cambia sin
 * turno abierto; aplica desde el próximo turno. Decisión D5 del 23/11/2026: el Supervisor lo cambia con la autorización del
 * Administrador (carné o usuario y contraseña).
 */
@Component({
  selector: 'cp-shift-mode-panel',
  template: `
    @if (loadError()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ loadError() }} <button class="link-button" type="button" (click)="load()">Reintentar</button></div> }
    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Modo de trabajo del turno</h3><p class="muted">La opción inicial es Turno individual. El Administrador la cambia; el Supervisor, con la autorización del Administrador.</p></div></div>
      @if (state(); as cash) {
        <div class="shift-modes" role="radiogroup" aria-label="Modo de trabajo del turno">
          @for (option of options; track option.mode) {
            <button type="button" class="shift-mode" role="radio" [attr.aria-checked]="selected === option.mode" [class.shift-mode--active]="selected === option.mode"
              [disabled]="saving() || cash.hasOpenShift" (click)="selected = option.mode">
              <span class="shift-mode__title"><span class="shift-mode__radio" aria-hidden="true"></span>{{ option.title }}
                <span class="badge" [class.badge--info]="cash.defaultShiftMode === option.mode">{{ cash.defaultShiftMode === option.mode ? 'Activo' : 'Alternativa' }}</span></span>
              <small>{{ option.subtitle }}</small>
              <ul>@for (point of option.points; track point) { <li>{{ point }}</li> }</ul>
              <span class="shift-mode__note" [class.shift-mode__note--warning]="option.mode === 'COMPARTIDO'"><strong>{{ option.noteTitle }}</strong>{{ option.note }}</span>
            </button>
          }
        </div>
        @if (cash.hasOpenShift) { <p class="field-hint" role="status">Hay un turno abierto: para cambiar el modo, ciérrelo primero en Caja. El cambio aplica desde el próximo turno.</p> }
        @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
        <div class="form-actions">
          <button class="button button--primary" type="button" (click)="save()" [disabled]="saving() || cash.hasOpenShift || selected === currentMode(cash)">{{ saving() ? 'Guardando…' : 'Guardar' }}</button>
          <button class="button button--secondary" type="button" (click)="selected = currentMode(cash)" [disabled]="saving() || selected === currentMode(cash)">Cancelar</button>
        </div>
      } @else if (!loadError()) { <p class="muted" role="status">Cargando…</p> }
    </section>`,
  styles: [`
    .shift-modes { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 16px; margin: 12px 0; }
    .shift-mode { display: flex; flex-direction: column; gap: 8px; padding: 18px; border: 1px solid var(--cp-border); border-radius: 12px;
      background: #fff; text-align: left; font: inherit; color: inherit; cursor: pointer; }
    .shift-mode:disabled { cursor: default; opacity: .85; }
    .shift-mode--active { border: 2px solid var(--cp-primary, #2a5ea8); }
    .shift-mode__title { display: flex; align-items: center; gap: 10px; font-size: 17px; font-weight: 700; }
    .shift-mode__radio { width: 18px; height: 18px; border: 2px solid #8a97a8; border-radius: 50%; }
    .shift-mode--active .shift-mode__radio { border: 5px solid var(--cp-primary, #2a5ea8); }
    .shift-mode small { color: var(--cp-text-muted); }
    .shift-mode ul { margin: 6px 0; padding-left: 18px; line-height: 1.7; }
    .shift-mode__note { display: grid; gap: 4px; padding: 12px 14px; border-radius: 10px; background: #edf6ef; color: #245a35; }
    .shift-mode__note--warning { background: #fff4dc; color: #7a5300; }
  `],
})
export class ShiftModePanel implements OnInit {
  private readonly cash = inject(CashApi);
  private readonly session = inject(SessionService);
  private readonly authorization = inject(AuthorizationDialog);
  readonly state = signal<CashState | null>(null);
  readonly loadError = toastSignal('error');
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  selected: Mode = 'INDIVIDUAL';
  readonly options: { mode: Mode; title: string; subtitle: string; points: string[]; noteTitle: string; note: string }[] = [
    { mode: 'INDIVIDUAL', title: 'Turno individual', subtitle: 'Seleccionado inicialmente',
      points: ['Una persona abre, opera y cierra la caja.', 'Todas las ventas quedan asociadas a esa persona.', 'No solicita carné entre ventas.', 'Flujo más simple para operación habitual.'],
      noteTitle: 'Recomendado para iniciar', note: 'Reduce configuración y capacitación inicial.' },
    { mode: 'COMPARTIDO', title: 'Turno compartido', subtitle: 'Disponible cuando varios empleados alternan en la caja.',
      points: ['Un responsable conserva el control contable.', 'Varios operadores pueden registrar ventas.', 'Cada venta exige escanear el carné.', 'El punto de venta se bloquea al terminar cada operación.'],
      noteTitle: 'Cambio controlado', note: 'Solo aplica a turnos nuevos; no altera el turno activo.' },
  ];

  ngOnInit(): void { void this.load(); }

  currentMode(cash: CashState): Mode { return cash.defaultShiftMode === 'COMPARTIDO' ? 'COMPARTIDO' : 'INDIVIDUAL'; }

  async load(): Promise<void> {
    this.loadError.set('');
    try { const cash = await this.cash.state(); this.state.set(cash); this.selected = this.currentMode(cash); }
    catch (e) { this.loadError.set(errorText(e)); }
  }

  async save(): Promise<void> {
    const cash = this.state();
    if (!cash || this.saving() || cash.hasOpenShift || this.selected === this.currentMode(cash) || cash.configurationVersion == null) return;
    const mode = this.selected, version = cash.configurationVersion;
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try {
      await this.authorization.open({ title: 'Autorizar cambio del modo de turno',
        description: 'El Supervisor cambia el modo del turno con la autorización del Administrador. Aplica desde el próximo turno.',
        detail: `Nuevo modo: ${mode === 'COMPARTIDO' ? 'Turno compartido' : 'Turno individual'}`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
        selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message),
        run: credentials => this.apply(mode, version, credentials) });
    } finally { this.saving.set(false); }
  }

  /** Lanza el error para que el diálogo de autorización lo muestre sin cerrarse. */
  private async apply(mode: Mode, version: number, credentials: AuthorizationCredentials | null): Promise<void> {
    const updated = await this.cash.updateShiftMode(mode, version, credentials ? authorizationBody(credentials) : null);
    this.state.set(updated); this.selected = this.currentMode(updated);
    this.notice.set(this.selected === 'COMPARTIDO'
      ? 'Turno compartido activado. Desde el próximo turno, cada venta se desbloquea con el carné del operador.'
      : 'Turno individual activado desde el próximo turno.');
  }
}
