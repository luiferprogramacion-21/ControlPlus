import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { ConfigApi, Numbering, NumberingKind, NUMBERING_KINDS } from './config-api';
import { toastSignal } from '../../core/toast';

interface Row { kind: NumberingKind; numbering: Numbering | null; error: string; }

/** Visible numbering of every document type and the next number the server will assign. */
@Component({
  selector: 'cp-numbering-panel',
  imports: [FormsModule],
  template: `
    <section class="panel module-panel">
      <div class="module-toolbar"><div><h3>Numeración de documentos</h3><p class="muted">El prefijo identifica el documento (VTA, ABO, CMP…). El sistema agrega la serie y asigna el siguiente número al registrarlo. «Número anterior» es la base del consecutivo: 0 indica que empieza en 1. Los documentos anteriores conservan su número. Los consecutivos se crean solos al instalar y al emitir el primer documento: aquí se consultan y, mientras no se haya emitido ninguno, se pueden ajustar.</p></div><button class="table-action" type="button" (click)="load()" [disabled]="loading()">Actualizar</button></div>
      <div class="table-wrap module-table-wrap"><table><thead><tr><th>Documento</th><th>Prefijo</th><th class="align-right">Número anterior</th><th class="align-right">Siguiente</th><th>Estado</th><th class="align-right">Acciones</th></tr></thead><tbody>
        @for (row of rows(); track row.kind.key) {
          <tr>
            <td><strong>{{ row.kind.label }}</strong>@if (row.numbering) { <small>Próximo: {{ visibleNumber(row.numbering) }}</small> }</td>
            <td class="numeric">{{ row.numbering?.prefix || '—' }}</td>
            <td class="align-right numeric">{{ row.numbering ? row.numbering.lastNumber : '—' }}</td>
            <td class="align-right numeric">{{ row.numbering ? row.numbering.nextNumber : '—' }}</td>
            <td>@if (row.error) { <span class="status-pill status-pill--warning">{{ row.error }}</span> } @else { <span class="status-pill" [class.status-pill--warning]="!row.numbering">{{ row.numbering ? 'Listo' : 'Se crea al usarlo' }}</span> }</td>
            <td class="align-right">
              @if (!row.numbering && !row.error) { <button class="table-action" type="button" (click)="open(row)">Configurar</button> }
              @if (canCorrect(row)) { <button class="table-action" type="button" (click)="open(row)">Ajustar</button> }
              @if (row.numbering && row.kind.canEdit && !canCorrect(row)) { <span class="muted">Ya no editable</span> }
            </td>
          </tr>
        } @empty { <tr class="empty-row"><td colspan="6">Cargando…</td></tr> }
      </tbody></table></div>
    </section>

    @if (editing(); as target) {
      <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="numbering-title"><div class="module-form-sheet__backdrop" (click)="close()"></div>
        <form class="panel panel--padded module-form" (submit)="$event.preventDefault(); save()">
          <div class="panel-heading"><h2 id="numbering-title">{{ target.numbering ? 'Ajustar' : 'Configurar' }} numeración de {{ target.kind.label.toLowerCase() }}</h2></div>
          <p class="muted">{{ target.numbering ? target.kind.editNote : 'Se crea una sola vez. ' + target.kind.editNote }} Los documentos ya creados no cambian.</p>
          <label class="field-label" for="numbering-prefix">Prefijo</label>
          <input id="numbering-prefix" class="text-field" name="prefix" [(ngModel)]="prefix" maxlength="10" [readOnly]="target.kind.fixedPrefix" autocomplete="off">
          @if (target.kind.fixedPrefix) { <p class="form-hint">El prefijo de este documento es fijo.</p> }
          <label class="field-label field-label--spaced" for="numbering-first">{{ target.numbering ? 'Próximo número' : 'Número inicial' }}</label>
          <input id="numbering-first" class="text-field" name="first" type="number" min="1" step="1" [(ngModel)]="first" [readOnly]="!target.numbering && target.kind.fixedPrefix && !target.kind.canEdit">
          <p class="form-hint">Formato del próximo documento: {{ preview(target) }}.</p>
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
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly formError = toastSignal('error');
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
    if (row.numbering && !this.canCorrect(row)) return;
    this.formError.set(''); this.notice.set('');
    this.prefix = row.numbering?.prefix ?? row.kind.defaultPrefix;
    this.first = row.numbering?.nextNumber ?? 1;
    this.editing.set(row);
  }
  close(): void { if (!this.saving()) this.editing.set(null); }

  canCorrect(row: Row): boolean {
    const numbering = row.numbering;
    // Older API builds omit canCorrect; only offer a correction when the stored base is still zero.
    return numbering !== null && row.kind.canEdit && (numbering.canCorrect ?? numbering.lastNumber === 0);
  }

  visibleNumber(numbering: Numbering): string {
    return `${numbering.prefix}-${numbering.series}-${numbering.nextNumber}`;
  }

  preview(row: Row): string {
    const prefix = (this.prefix || row.kind.defaultPrefix).trim().toUpperCase();
    const series = row.numbering?.series ?? '[serie]';
    const first = String(Math.max(1, Number(this.first) || 1));
    return `${prefix}-${series}-${first}`;
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
    const prefix = this.prefix.trim().toUpperCase(); const first = Number(this.first);
    if (row.numbering && prefix === row.numbering.prefix && first === row.numbering.nextNumber) {
      this.formError.set('No hay cambios por guardar.'); return;
    }
    this.saving.set(true); this.formError.set('');
    try {
      if (row.numbering) await this.api.updateNumbering(row.kind, prefix, first, row.numbering.version);
      else await this.api.createNumbering(row.kind, prefix, first);
      this.editing.set(null); this.notice.set(`Numeración de ${row.kind.label.toLowerCase()} guardada. El cambio se aplicará a documentos nuevos.`); await this.load();
    } catch (e) { this.formError.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
