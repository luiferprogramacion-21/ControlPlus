import { Component, inject, OnInit, signal } from '@angular/core';
import { errorText } from '../../core/api';
import { ConfigApi, ReasonGroup, ReasonItem, REASON_GROUPS } from './config-api';

interface GroupState { group: ReasonGroup; reasons: ReasonItem[] | null; error: string; busy: boolean; }

/** Approved reasons used when annulling or closing documents. The catalog is created explicitly, never silently. */
@Component({
  selector: 'cp-reasons-panel',
  template: `
    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Motivos</h3><p class="muted">Sin motivos inicializados no se pueden anular ventas ni compras, ni cerrar o cancelar pedidos. Inicializar solo crea los que faltan.</p></div><button class="table-action" type="button" (click)="load()">Actualizar</button></div>
      @if (notice()) { <div class="inline-feedback inline-feedback--success" role="status">{{ notice() }}</div> }
      <div class="table-wrap module-table-wrap"><table><thead><tr><th>Uso</th><th>Motivos</th><th>Estado</th><th class="align-right">Acciones</th></tr></thead><tbody>
        @for (state of states(); track state.group.key) {
          <tr>
            <td><strong>{{ state.group.label }}</strong></td>
            <td>@if (state.reasons?.length) { {{ names(state) }} } @else { — }</td>
            <td>@if (state.error) { <span class="status-pill status-pill--warning">{{ state.error }}</span> } @else if (state.reasons === null) { Cargando… } @else { <span class="status-pill" [class.status-pill--warning]="!state.reasons.length">{{ state.reasons.length ? state.reasons.length + ' disponibles' : 'Sin inicializar' }}</span> }</td>
            <td class="align-right"><button class="table-action" type="button" (click)="initialize(state)" [disabled]="state.busy || state.reasons === null">{{ state.busy ? 'Inicializando…' : (state.reasons?.length ? 'Completar faltantes' : 'Inicializar') }}</button></td>
          </tr>
        }
      </tbody></table></div>
    </section>`,
})
export class ReasonsPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly states = signal<GroupState[]>(REASON_GROUPS.map(group => ({ group, reasons: null, error: '', busy: false })));
  readonly notice = signal('');

  ngOnInit(): void { void this.load(); }

  names(state: GroupState): string { return (state.reasons ?? []).map(reason => reason.name).join(', '); }
  private patch(key: string, change: Partial<GroupState>): void { this.states.update(states => states.map(s => s.group.key === key ? { ...s, ...change } : s)); }

  async load(): Promise<void> {
    await Promise.all(REASON_GROUPS.map(async group => {
      try { this.patch(group.key, { reasons: await this.api.reasons(group), error: '' }); }
      catch (e) { this.patch(group.key, { reasons: [], error: errorText(e) }); }
    }));
  }

  async initialize(state: GroupState): Promise<void> {
    if (state.busy) return;
    this.notice.set(''); this.patch(state.group.key, { busy: true, error: '' });
    try {
      const reasons = await this.api.initializeReasons(state.group);
      this.patch(state.group.key, { reasons, busy: false });
      this.notice.set(`Motivos de «${state.group.label.toLowerCase()}» listos (${reasons.length}).`);
    } catch (e) { this.patch(state.group.key, { busy: false, error: errorText(e) }); }
  }
}
