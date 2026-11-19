import { DatePipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, Injectable, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { firstValueFrom } from 'rxjs';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AdminApi, AdminUser } from './admin-api';
import { AUDIT_ACTION_GROUPS, auditActionLabel, auditActionModule } from './audit-actions';
import { toastSignal } from '../../core/toast';
import { groupedNumber } from '../../shared/number-input';

/** Localize measured values for display, keeping identifiers and concurrency versions literal. */
function displayAuditValue(value: unknown, field = ''): unknown {
  const lastWord = field.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase().split(/[_-]/).at(-1) ?? '';
  if (/^(id|ids|code|codes|codigo|barcode|number|numero|version|phone|telephone|telefono|identification|identificacion|nit|document|documento|reference|referencia)$/.test(lastWord)) return value;
  if (typeof value === 'number') return groupedNumber(value);
  if (Array.isArray(value)) return value.map(item => displayAuditValue(item));
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, displayAuditValue(item, key)]));
  }
  return value;
}

/** One audit event as GET /api/audit-records returns it (only sanitized data, never secrets). */
export interface AuditRecord {
  id: string; occurredAtUtc: string; actorUserId: string | null; actorUserName: string | null;
  actionCode: string; entityType: string; entityId: string | null; details: string | null; correlationId: string | null;
  authorizerUserId: string | null; authorizerUserName: string | null; reason: string | null; result: string;
}

export interface AuditFilter { from: string; to: string; userId: string; action: string; page: number; pageSize: number; }

@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  /** Dates are local calendar days; the server expects UTC instants, so each day is sent from 00:00 to 23:59:59.999 local time. */
  records(filter: AuditFilter): Promise<Page<AuditRecord>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.from) params = params.set('fromUtc', localDay(filter.from, false));
    if (filter.to) params = params.set('toUtc', localDay(filter.to, true));
    if (filter.userId) params = params.set('actorUserId', filter.userId);
    if (filter.action) params = params.set('action', filter.action);
    return firstValueFrom(this.http.get<Page<AuditRecord>>('/api/audit-records', { params }));
  }
}

export function localDay(iso: string, endOfDay: boolean): string {
  const [year, month, day] = iso.split('-').map(Number);
  const value = endOfDay ? new Date(year, month - 1, day, 23, 59, 59, 999) : new Date(year, month - 1, day, 0, 0, 0, 0);
  return value.toISOString();
}

function today(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

/** Read-only audit log (approved 16 November 2026): who did what, when, who authorized it and why. */
@Component({
  selector: 'cp-audit-log',
  imports: [DatePipe, FormsModule, MatPaginatorModule],
  templateUrl: './audit-log.html',
  styleUrls: ['../directory.scss'],
  styles: [`
    .directory-filters .audit-date { width: auto; min-width: 150px; }
    .audit-detail td { background: var(--cp-surface-muted); }
    .audit-detail dl { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 6px 18px; margin: 0; }
    .audit-detail dl div { display: flex; flex-direction: column; min-width: 0; }
    .audit-detail dt { color: var(--cp-text-muted); font-size: 12px; }
    .audit-detail dd { margin: 0; overflow-wrap: anywhere; }
  `],
})
export class AuditLog implements OnInit {
  private readonly api = inject(AuditApi);
  private readonly admin = inject(AdminApi);
  readonly session = inject(SessionService);
  /** Each module's actions sorted by their Spanish name for the filter. */
  readonly groups = AUDIT_ACTION_GROUPS.map((group) => ({
    module: group.module,
    actions: Object.entries(group.actions).map(([code, label]) => ({ code, label })).sort((a, b) => a.label.localeCompare(b.label, 'es')),
  }));
  readonly records = signal<Page<AuditRecord> | null>(null);
  readonly users = signal<AdminUser[]>([]);
  readonly expanded = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = toastSignal('error');
  filter: AuditFilter = { from: today(), to: today(), userId: '', action: '', page: 1, pageSize: 50 };
  private requestId = 0;

  /** The user filter needs the user list, which only USERS.READ may read. */
  get canFilterUsers(): boolean { return this.session.has('USERS.READ'); }

  async ngOnInit(): Promise<void> {
    if (this.canFilterUsers) {
      try { this.users.set((await this.admin.users({ page: 1, pageSize: 100 })).items); }
      catch { this.users.set([]); }
    }
    await this.reload();
  }

  async reload(): Promise<void> {
    if (this.filter.from && this.filter.to && this.filter.from > this.filter.to) {
      this.error.set('La fecha inicial no puede ser posterior a la final.');
      return;
    }
    const id = ++this.requestId;
    this.loading.set(true); this.error.set(''); this.expanded.set(null);
    try {
      const data = await this.api.records(this.filter);
      if (id === this.requestId) this.records.set(data);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  find(): void { this.filter.page = 1; void this.reload(); }
  pageChanged(event: PageEvent): void { this.filter.page = event.pageIndex + 1; this.filter.pageSize = event.pageSize; void this.reload(); }
  toggle(record: AuditRecord): void { this.expanded.set(this.expanded() === record.id ? null : record.id); }

  actionLabel(record: AuditRecord): string { return auditActionLabel(record.actionCode); }
  moduleLabel(record: AuditRecord): string { return auditActionModule(record.actionCode); }
  /** Rejections and failures stand out; any other result reads as completed. */
  isProblem(record: AuditRecord): boolean { return /RECHAZ|FALL|ERROR|DENEG/i.test(record.result); }
  resultLabel(record: AuditRecord): string {
    const value = record.result.trim();
    return value ? value.charAt(0).toUpperCase() + value.slice(1).toLowerCase().replace(/_/g, ' ') : '';
  }
  entityLabel(record: AuditRecord): string {
    const value = record.entityType.replace(/_/g, ' ').toLowerCase();
    return value.charAt(0).toUpperCase() + value.slice(1);
  }

  /** Details are sanitized JSON written by the server; shown as field and value, or as plain text if not JSON. */
  detailRows(record: AuditRecord): { field: string; value: string }[] {
    if (!record.details) return [];
    try {
      const parsed: unknown = JSON.parse(record.details);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        return Object.entries(parsed as Record<string, unknown>).map(([field, value]) => ({
          field, value: value === null ? '—' : typeof value === 'object' ? JSON.stringify(displayAuditValue(value, field)) : String(displayAuditValue(value, field)),
        }));
      }
      return [{ field: 'Detalle', value: typeof parsed === 'number' ? groupedNumber(parsed) : JSON.stringify(displayAuditValue(parsed)) }];
    } catch { return [{ field: 'Detalle', value: record.details }]; }
  }
}
