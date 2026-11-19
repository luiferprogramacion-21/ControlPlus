import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { Supplier, SupplierInput, SuppliersApi } from './suppliers-api';
import { toastSignal } from '../../core/toast';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { WindowSheet } from '../../shared/window-sheet';
@Component({ selector: 'cp-suppliers', imports: [WindowSheet, FormsModule, MatPaginatorModule, Icon], templateUrl: './suppliers.html', styleUrl: '../directory.scss' })
export class Suppliers implements OnInit {
  private readonly api = inject(SuppliersApi);
  private readonly confirmation = inject(ConfirmationDialog);
  private readonly owner = inject(DestroyRef);
  readonly session = inject(SessionService);
  readonly result = signal<Page<Supplier> | null>(null);
  readonly selected = signal<Supplier | null>(null);
  readonly editing = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  name = ''; documentType = ''; documentNumber = ''; status: 'active' | 'inactive' | 'all' = 'active'; page = 1; pageSize = 20;
  form: SupplierInput = this.empty();
  private requestId = 0;
  get canManage(): boolean { return this.session.has('SUPPLIERS.MANAGE'); }
  ngOnInit(): void { void this.reload(); }
  private empty(): SupplierInput { return { documentType: null, documentNumber: null, name: '', phone: null, address: null, email: null, observations: null }; }
  async reload(): Promise<void> { const id = ++this.requestId; this.loading.set(true); this.error.set(''); try { const data = await this.api.list({ name: this.name.trim(), documentType: this.documentType.trim(), documentNumber: this.documentNumber.trim(), isActive: this.status === 'all' ? null : this.status === 'active', page: this.page, pageSize: this.pageSize }); if (id === this.requestId) this.result.set(data); } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); } finally { if (id === this.requestId) this.loading.set(false); } }
  find(): void { this.page = 1; void this.reload(); }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  create(): void { if (!this.canManage) return; this.selected.set(null); this.form = this.empty(); this.editing.set(true); this.error.set(''); }
  edit(s: Supplier): void { if (!this.canManage) return; this.selected.set(s); this.form = { documentType: s.documentType, documentNumber: s.documentNumber, name: s.name, phone: s.phone, address: s.address, email: s.email, observations: s.observations }; this.editing.set(true); this.error.set(''); }
  close(): void { this.editing.set(false); this.selected.set(null); }
  private normalized(): SupplierInput { const f = this.form; const trim = (v: string | null) => v?.trim() || null; return { documentType: trim(f.documentType)?.toUpperCase() ?? null, documentNumber: trim(f.documentNumber)?.toUpperCase() ?? null, name: f.name.trim(), phone: trim(f.phone), address: trim(f.address), email: trim(f.email)?.toLowerCase() ?? null, observations: trim(f.observations) }; }
  validation(): string { const f = this.normalized(); if (!f.name || f.name.length > 200) return 'Indica un nombre de hasta 200 caracteres.'; if (!!f.documentType !== !!f.documentNumber || (f.documentType?.length ?? 0) > 20 || (f.documentNumber?.length ?? 0) > 50) return 'Indica tipo y número de documento juntos.'; if ((f.phone?.length ?? 0) > 30 || (f.address?.length ?? 0) > 300) return 'Revisa el teléfono o la dirección.'; if (f.email && (f.email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(f.email))) return 'Revisa el correo.'; return ''; }
  async save(): Promise<void> { if (!this.canManage || this.saving()) return; const issue = this.validation(); if (issue) { this.error.set(issue); return; } this.saving.set(true); this.error.set(''); try { const selected = this.selected(); if (selected) await this.api.update(selected, this.normalized()); else await this.api.create(this.normalized()); this.close(); this.notice.set('Proveedor guardado.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async refreshSelected(): Promise<void> { const selected = this.selected(); if (!selected) return; try { this.edit(await this.api.get(selected.id)); } catch (e) { this.error.set(errorText(e)); } }
  async toggle(s: Supplier): Promise<void> {
    if (!this.canManage || this.saving()) return;
    const action = s.isActive ? 'Inactivar' : 'Activar';
    if (!await this.confirmation.confirm({ title: `${action} proveedor`, message: `¿${action} a ${s.name}?`, confirmText: action, danger: s.isActive }, this.owner)) return;
    if (this.owner.destroyed || !this.canManage || this.saving()) return;
    this.saving.set(true); this.error.set('');
    try { await this.api.status(s); this.notice.set('Estado actualizado.'); await this.reload(); }
    catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); }
  }
}
