import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ConfigApi, Numbering, NumberingKind, NUMBERING_KINDS } from './config-api';

interface Row { kind: NumberingKind; numbering: Numbering | null; error: string; }

/** Visible numbering of every document type: prefix, last used number and next number. */
@Component({
  selector: 'cp-numbering-panel',
  imports: [FormsModule],
  template: `
    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Numeración de documentos</h3><p class="muted">Cada tipo de documento lleva su propio consecutivo visible. Un documento sin consecutivo configurado no se puede registrar.</p></div><button class="table-action" type="button" (click)="load()" [disabled]="loading()">Actualizar</button></div>
      @if (notice()) { <div class="inline-feedback inline-feedback--success" role="status">{{ notice() }}</div> }
      @if (error()) { <div class="inline-feedback inline-feedback--error" role="alert">{{ error() }}</div> }
      <div class="table-wrap module-table-wrap"><table><thead><tr><th>Documento</th><th>Prefijo</th><th class="align-right">Último usado</th><th class="align-right">Siguiente</th><th>Estado</th><th class="align-right">Acciones</th></tr></thead><tbody>
        @for (row of rows(); track row.kind.key) {
          <tr>
            <td><strong>{{ row.kind.label }}</strong><small>{{ row.kind.sample }}</small></td>
            <td class="numeric">{{ row.numbering?.prefix || '—' }}</td>
            <td class="align-right numeric">{{ row.numbering ? row.numbering.lastNumber : '—' }}</td>
            <td class="align-right numeric">{{ row.numbering ? row.numbering.nextNumber : '—' }}</td>
            <td>@if (row.error) { <span class="status-pill status-pill--warning">{{ row.error }}</span> } @else { <span class="status-pill" [class.status-pill--warning]="!row.numbering">{{ row.numbering ? 'Configurado' : 'Sin configurar' }}</span> }</td>
            <td class="align-right">
              @if (!row.numbering && !row.error) { <button class="table-action" type="button" (click)="open(row)">Configurar</button> }
              @if (row.numbering && row.kind.canEdit) { <button class="table-action" type="button" (click)="open(row)">Corregir</button> }
            </td>
          </tr>
        } @empty { <tr class="empty-row"><td colspan="6">Cargando…</td></tr> }
      </tbody></table></div>
    </section>

    @if (editing(); as target) {
      <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="numbering-title"><div class="module-form-sheet__backdrop" (click)="close()"></div>
        <form class="panel panel--padded module-form" (submit)="$event.preventDefault(); save()">
          <div class="panel-heading"><h2 id="numbering-title">{{ target.numbering ? 'Corregir' : 'Configurar' }} numeración de {{ target.kind.label.toLowerCase() }}</h2></div>
          <p class="muted">{{ target.numbering ? target.kind.editNote : 'Se crea una sola vez. ' + target.kind.editNote }}</p>
          <label class="field-label" for="numbering-prefix">Prefijo</label>
          <input id="numbering-prefix" class="text-field" name="prefix" [(ngModel)]="prefix" maxlength="10" [readOnly]="target.kind.fixedPrefix" autocomplete="off">
          @if (target.kind.fixedPrefix) { <p class="form-hint">El prefijo de este documento es fijo.</p> }
          <label class="field-label field-label--spaced" for="numbering-first">Primer número</label>
          <input id="numbering-first" class="text-field" name="first" type="number" min="1" step="1" [(ngModel)]="first" [readOnly]="!target.numbering && target.kind.fixedPrefix && !target.kind.canEdit">
          <p class="form-hint">El primer documento será {{ preview(target) }}.</p>
          @if (formError()) { <p class="field-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions"><button class="button button--secondary" type="button" (click)="close()" [disabled]="saving()">Cancelar</button><button class="button button--primary" type="submit" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar' }}</button></div>
        </form>
      </div>
    }`,
})
export class NumberingPanel implements OnInit {
  private readonly api = inject(ConfigApi);
  readonly rows = signal<Row[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly formError = signal('');
  readonly editing = signal<Row | null>(null);
  prefix = '';
  first = 1;

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    const rows = await Promise.all(NUMBERING_KINDS.map(async (kind): Promise<Row> => {
      try { return { kind, numbering: await this.api.numbering(kind), error: '' }; }
      catch (e) { return { kind, numbering: null, error: errorText(e) }; }
    }));
    this.rows.set(rows); this.loading.set(false);
  }

  open(row: Row): void {
    this.formError.set(''); this.notice.set('');
    this.prefix = row.numbering?.prefix ?? row.kind.defaultPrefix;
    this.first = row.numbering?.nextNumber ?? 1;
    this.editing.set(row);
  }
  close(): void { if (!this.saving()) this.editing.set(null); }

  preview(row: Row): string {
    const prefix = (this.prefix || row.kind.defaultPrefix).trim().toUpperCase();
    const series = row.numbering?.series ?? '001';
    return `${prefix}-${series}-${String(Math.max(1, Number(this.first) || 1)).padStart(6, '0')}`;
  }

  validation(row: Row): string {
    const prefix = this.prefix.trim();
    if (!prefix) return 'El prefijo es obligatorio.';
    if (prefix.length > 10) return 'El prefijo admite hasta 10 caracteres.';
    if (row.kind.fixedPrefix && prefix.toUpperCase() !== row.kind.defaultPrefix) return `El prefijo de este documento es ${row.kind.defaultPrefix}.`;
    const first = Number(this.first);
    if (!Number.isSafeInteger(first) || first < 1) return 'El primer número debe ser un entero mayor que cero.';
    if (!row.numbering && row.kind.fixedPrefix && !row.kind.canEdit && first !== 1) return 'La numeración inicial de este documento empieza en 1.';
    return '';
  }

  async save(): Promise<void> {
    const row = this.editing(); if (!row || this.saving()) return;
    const issue = this.validation(row); if (issue) { this.formError.set(issue); return; }
    this.saving.set(true); this.formError.set('');
    const prefix = this.prefix.trim().toUpperCase(); const first = Number(this.first);
    try {
      if (row.numbering) await this.api.updateNumbering(row.kind, prefix, first, row.numbering.version);
      else await this.api.createNumbering(row.kind, prefix, first);
      this.editing.set(null); this.notice.set(`Numeración de ${row.kind.label.toLowerCase()} guardada.`); await this.load();
    } catch (e) { this.formError.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
