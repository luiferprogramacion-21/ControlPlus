import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { PrintJob, printJobStatusText, PrintProfile, SalesApi } from '../sales/sales-api';

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
  readonly error = signal('');
  readonly formError = signal('');
  readonly notice = signal('');
  form = { name: '', systemQueueName: '', columns: 32, makeDefault: true };

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

  startCreate(): void { this.form = { name: '', systemQueueName: '', columns: 32, makeDefault: true }; this.formError.set(''); this.creating.set(true); }

  validation(): string {
    const name = this.form.name.trim(), queue = this.form.systemQueueName.trim();
    if (!name || name.length > 150) return 'El nombre es obligatorio y admite hasta 150 caracteres.';
    if (!queue || queue.length > 300) return 'El nombre de la cola de Windows es obligatorio y admite hasta 300 caracteres.';
    if (!Number.isInteger(this.form.columns) || this.form.columns < 24 || this.form.columns > 80) return 'Las columnas deben ser un entero entre 24 y 80.';
    return '';
  }

  async save(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.formError.set(issue); return; }
    this.saving.set(true); this.formError.set('');
    try {
      await this.api.createPrintProfile({ name: this.form.name.trim(), systemQueueName: this.form.systemQueueName.trim(), columns: this.form.columns, makeDefault: this.form.makeDefault });
      this.creating.set(false);
      this.notice.set('Perfil creado. No se comprobó que la cola exista en Windows; verifica el nombre antes de imprimir.');
      await this.reload();
    } catch (e) { this.formError.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  async retry(job: PrintJob): Promise<void> {
    this.error.set(''); this.notice.set('');
    try { await this.api.retryAdminPrintJob(job.id); this.notice.set('Trabajo enviado de nuevo a la cola.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); }
  }
}
