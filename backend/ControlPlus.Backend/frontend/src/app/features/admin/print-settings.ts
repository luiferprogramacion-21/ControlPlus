import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { PrintJob, printJobStatusText, PrintProfile, SalesApi } from '../sales/sales-api';
import { toastSignal } from '../../core/toast';

/** Perfiles de impresora (cola de Windows) y cola de impresión. Crear un perfil no comprueba que la cola exista. */
@Component({
  selector: 'cp-print-settings',
  imports: [DatePipe, FormsModule],
  templateUrl: './print-settings.html',
})
export class PrintSettings implements OnInit {
  private readonly api = inject(SalesApi);
  readonly session = inject(SessionService);
  readonly profiles = signal<PrintProfile[]>([]);
  readonly jobs = signal<PrintJob[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly creating = signal(false);
  readonly deleting = signal(false);
  readonly error = toastSignal('error');
  readonly formError = signal('');
  readonly notice = toastSignal('success');
  form: { name: string; systemQueueName: string; columns: number; makeDefault: boolean; printerType: 'RECIBOS' | 'ETIQUETAS' | 'HIBRIDA'; labelWidthMm: number | null; labelHeightMm: number | null; resolutionDpi: number | null } = {
    name: '', systemQueueName: '', columns: 32, makeDefault: true,
    printerType: 'RECIBOS', labelWidthMm: null, labelHeightMm: null, resolutionDpi: null,
  };

  ngOnInit(): void { void this.reload(); }
  jobText(job: PrintJob): string { return printJobStatusText(job); }
  copyLabel(job: PrintJob): string { return job.copyType === 'ORIGINAL' ? 'Original' : `Reimpresión ${job.copyNumber}`; }
  canRetry(job: PrintJob): boolean { return job.status === 'ERROR' && this.session.has('CONFIGURATION.MANAGE'); }

  async reload(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const [profiles, jobs] = await Promise.all([this.api.printProfiles(), this.api.printQueue(1, 20)]);
      this.profiles.set(profiles); this.jobs.set(jobs.items);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  startCreate(): void {
    this.form = { name: '', systemQueueName: '', columns: 32, makeDefault: true, printerType: 'RECIBOS', labelWidthMm: null, labelHeightMm: null, resolutionDpi: null };
    this.formError.set(''); this.creating.set(true);
  }

  validation(): string {
    const name = this.form.name.trim(), queue = this.form.systemQueueName.trim();
    if (!name || name.length > 150) return 'El nombre es obligatorio y admite hasta 150 caracteres.';
    if (!queue || queue.length > 300) return 'El nombre de la cola de Windows es obligatorio y admite hasta 300 caracteres.';
    if (this.form.printerType !== 'ETIQUETAS' && (!Number.isInteger(this.form.columns) || this.form.columns < 24 || this.form.columns > 80)) return 'Las columnas deben ser un entero entre 24 y 80.';
    const dimensions = [this.form.labelWidthMm, this.form.labelHeightMm];
    if ((dimensions[0] == null) !== (dimensions[1] == null)) return 'Indica juntos el ancho y el alto de la etiqueta.';
    if (dimensions.some(value => value != null && (!Number.isFinite(value) || value <= 0 || value > 500))) return 'Las dimensiones deben estar entre 0 y 500 mm.';
    if (this.form.resolutionDpi != null && (!Number.isInteger(this.form.resolutionDpi) || this.form.resolutionDpi <= 0 || this.form.resolutionDpi > 2400)) return 'La resolución debe ser un entero entre 1 y 2400 DPI.';
    return '';
  }

  async save(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.formError.set(issue); return; }
    this.saving.set(true); this.formError.set('');
    try {
      await this.api.createPrintProfile({
        name: this.form.name.trim(), systemQueueName: this.form.systemQueueName.trim(), columns: this.form.columns,
        makeDefault: this.form.makeDefault, printerType: this.form.printerType,
        labelWidthMm: this.form.printerType === 'RECIBOS' ? null : this.form.labelWidthMm,
        labelHeightMm: this.form.printerType === 'RECIBOS' ? null : this.form.labelHeightMm,
        resolutionDpi: this.form.resolutionDpi,
      });
      this.creating.set(false);
      this.notice.set('Perfil creado. No se comprobó que la cola exista en Windows; verifica el nombre antes de imprimir.');
      await this.reload();
    } catch (e) { this.formError.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  /** Decisión del 18 de noviembre de 2026: se borra el perfil; si ya imprimió, se retira y se conserva su historial. */
  async remove(profile: PrintProfile): Promise<void> {
    if (!this.session.has('CONFIGURATION.MANAGE') || this.deleting() ||
      !confirm(`¿Eliminar el perfil «${profile.name}»?${profile.isDefault ? ' Es el predeterminado: mientras no haya otro, no se imprimirá desde el sistema.' : ''}`)) return;
    this.deleting.set(true); this.error.set(''); this.notice.set('');
    try {
      const result = await this.api.deletePrintProfile(profile.id);
      this.notice.set(result.physicallyDeleted ? 'Perfil eliminado.' : 'Perfil eliminado. Se conserva el historial de lo que imprimió.');
      await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.deleting.set(false); }
  }

  async retry(job: PrintJob): Promise<void> {
    this.error.set(''); this.notice.set('');
    try { await this.api.retryAdminPrintJob(job.id); this.notice.set('Trabajo enviado de nuevo a la cola.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); }
  }
}
