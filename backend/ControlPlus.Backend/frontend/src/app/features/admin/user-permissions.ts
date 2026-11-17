import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { AdminApi, AdminPermission, AdminUser, EffectivePermission } from './admin-api';
import { isVisibleAdminPermission } from './visible-permissions';

interface Row extends EffectivePermission { module: string; }

@Component({
  selector: 'cp-user-permissions',
  imports: [FormsModule],
  styleUrl: './admin.scss',
  template: `
    <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="user-permissions-title"><div class="module-form-sheet__backdrop" (click)="closed.emit()"></div>
      <div class="panel panel--padded module-form permissions-sheet">
        <div class="panel-heading"><h2 id="user-permissions-title">Permisos de {{ user().displayName }}</h2></div>
        <p class="muted">{{ user().userName }} · Los permisos parten de la plantilla de su rol; aquí se añaden concesiones o revocaciones individuales. La revocación individual tiene precedencia. Cada cambio cierra las sesiones de este usuario.</p>
        <div class="permissions-toolbar">
          <label class="sr-only" for="permission-filter">Filtrar permisos</label>
          <input id="permission-filter" class="text-field" name="filter" [ngModel]="filter()" (ngModelChange)="filter.set($event)" placeholder="Filtrar por nombre o código">
          <button class="button button--secondary" type="button" [disabled]="saving() || !exceptionCount()" (click)="reset()">Restablecer excepciones ({{ exceptionCount() }})</button>
        </div>
        @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
        @if (loading()) { <p class="muted" role="status">Cargando permisos…</p> }
        <div class="permissions-list">
          @for (group of groups(); track group.module) {
            <h4>{{ group.module }}</h4>
            <ul>
              @for (row of group.rows; track row.id) {
                <li [class.permission--exception]="row.source === 'INDIVIDUAL'">
                  <span><strong>{{ row.name }}</strong> <small>({{ row.code }})</small><br>
                    <small>{{ row.granted ? 'Permitido' : 'No permitido' }} · {{ origin(row) }}</small></span>
                  <button class="table-action" type="button" [disabled]="saving()" (click)="toggle(row)">{{ row.granted ? 'Revocar' : 'Conceder' }}</button>
                </li>
              }
            </ul>
          } @empty { @if (!loading()) { <p class="muted">No hay permisos para este filtro.</p> } }
        </div>
        <div class="form-actions"><button class="button button--secondary" type="button" (click)="closed.emit()">Cerrar</button></div>
      </div>
    </div>`,
})
export class UserPermissions implements OnInit {
  private readonly api = inject(AdminApi);
  readonly user = input.required<AdminUser>();
  readonly catalog = input.required<AdminPermission[]>();
  readonly closed = output<void>();
  readonly effective = signal<EffectivePermission[]>([]);
  readonly filter = signal('');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');

  readonly rows = computed<Row[]>(() => {
    const modules = new Map(this.catalog().map(permission => [permission.id, permission.module]));
    return this.effective()
      .filter(item => isVisibleAdminPermission(item.code))
      .map(item => ({ ...item, module: modules.get(item.id) ?? 'OTROS' }));
  });
  readonly exceptionCount = computed(() => this.effective().filter(item => item.source === 'INDIVIDUAL').length);
  readonly groups = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const visible = this.rows().filter(row => !term || row.name.toLowerCase().includes(term) || row.code.toLowerCase().includes(term));
    const map = new Map<string, Row[]>();
    for (const row of visible) map.set(row.module, [...(map.get(row.module) ?? []), row]);
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([module, rows]) => ({ module, rows }));
  });

  ngOnInit(): Promise<void> { return this.load(); }

  origin(row: EffectivePermission): string {
    if (row.source !== 'INDIVIDUAL') return 'Por rol';
    return row.individualEffect === 'REVOCAR' ? 'Revocado individualmente' : 'Concedido individualmente';
  }

  private async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { this.effective.set(await this.api.effectivePermissions(this.user().id)); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  async toggle(row: Row): Promise<void> {
    if (this.saving()) return;
    const next = !row.granted;
    if (!confirm(`${next ? 'Conceder' : 'Revocar'} "${row.name}" a ${this.user().displayName}? Sus sesiones abiertas se invalidarán.`)) return;
    await this.mutate(() => this.api.setPermissionOverride(this.user().id, row.id, next));
  }

  async reset(): Promise<void> {
    if (this.saving() || !this.exceptionCount() || !confirm(`Eliminar las ${this.exceptionCount()} excepciones individuales de ${this.user().displayName}? Sus permisos volverán a depender solo de su rol y sus sesiones se invalidarán.`)) return;
    await this.mutate(() => this.api.resetPermissionOverrides(this.user().id));
  }

  private async mutate(action: () => Promise<void>): Promise<void> {
    this.saving.set(true); this.error.set('');
    try { await action(); await this.load(); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
