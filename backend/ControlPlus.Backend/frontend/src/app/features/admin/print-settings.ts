import { NumberInput } from '../../shared/number-input';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { PrintJob, printJobStatusText, PrintProfile, SalesApi } from '../sales/sales-api';
import { toastSignal } from '../../core/toast';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { WindowSheet } from '../../shared/window-sheet';
import { HelpTip } from '../../shared/help-tip';

/** Perfiles de impresora (cola de Windows) y cola de impresión. Crear un perfil no comprueba que la cola exista. */
@Component({
  selector: 'cp-print-settings',
  imports: [WindowSheet, HelpTip, NumberInput, DatePipe, FormsModule],
  templateUrl: './print-settings.html',
  styleUrl: './print-settings.scss',
})
export class PrintSettings implements OnInit {
  private readonly api = inject(SalesApi);
  private readonly confirmations = inject(ConfirmationDialog);
  private readonly destroyRef = inject(DestroyRef);
  private confirming = false;
  readonly session = inject(SessionService);
  readonly profiles = signal<PrintProfile[]>([]);
  readonly jobs = signal<PrintJob[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly creating = signal(false);
  readonly deleting = signal(false);
  readonly drawerEditing = signal<PrintProfile | null>(null);
  readonly drawerSaving = signal(false);
  readonly error = toastSignal('error');
  readonly formError = toastSignal('error');
  readonly notice = toastSignal('success');
  form: { name: string; systemQueueName: string; columns: number; makeDefault: boolean; printerType: 'RECIBOS' | 'ETIQUETAS' | 'HIBRIDA'; labelWidthMm: number | null; labelHeightMm: number | null; resolutionDpi: number | null } = {
    name: '', systemQueueName: '', columns: 32, makeDefault: true,
    printerType: 'RECIBOS', labelWidthMm: null, labelHeightMm: null, resolutionDpi: null,
  };
  drawerForm: { enabled: boolean; pin: number; pulseOnMs: number; pulseOffMs: number } = {
    enabled: false, pin: 2, pulseOnMs: 100, pulseOffMs: 100,
  };

  ngOnInit(): void { void this.reload(); }
  jobText(job: PrintJob): string { return printJobStatusText(job); }
  copyLabel(job: PrintJob): string { return job.copyType === 'ORIGINAL' ? 'Original' : `Reimpresión ${job.copyNumber}`; }
  canRetry(job: PrintJob): boolean { return job.status === 'ERROR' && this.session.has('CONFIGURATION.MANAGE'); }
  canConfigureDrawer(profile: PrintProfile): boolean { return profile.active && profile.printerType !== 'ETIQUETAS' && this.session.has('CONFIGURATION.MANAGE'); }
  drawerLabel(profile: PrintProfile): string {
    return profile.printerType === 'ETIQUETAS' ? 'No aplica' : profile.drawerEnabled ? 'Habilitado' : 'Desactivado';
  }

  startConfigureDrawer(profile: PrintProfile): void {
    if (!this.canConfigureDrawer(profile) || this.saving() || this.deleting() || this.drawerSaving() || this.confirming) return;
    this.drawerForm = {
      enabled: profile.drawerEnabled, pin: profile.drawerPin ?? 2,
      pulseOnMs: profile.drawerPulseOnMs ?? 100, pulseOffMs: profile.drawerPulseOffMs ?? 100,
    };
    this.formError.set(''); this.drawerEditing.set(profile);
  }

  closeConfigureDrawer(): void {
    if (this.drawerSaving() || this.confirming) return;
    this.drawerEditing.set(null); this.formError.set('');
  }

  async saveDrawer(): Promise<void> {
    const profile = this.drawerEditing();
    if (!profile || !this.canConfigureDrawer(profile) || this.drawerSaving() || this.confirming) return;
    const { enabled, pin, pulseOnMs, pulseOffMs } = this.drawerForm;
    if (![2, 5].includes(pin)) { this.formError.set('Elige el pin 2 o el pin 5.'); return; }
    if ([pulseOnMs, pulseOffMs].some(value => !Number.isInteger(value) || value < 20 || value > 500 || value % 2 !== 0)) {
      this.formError.set('Los tiempos deben ser pares entre 20 y 500 ms.'); return;
    }
    if (enabled && !profile.drawerEnabled) {
      this.confirming = true;
      let approved: boolean;
      try {
        approved = await this.confirmations.confirm({
          title: 'Habilitar el cajón de dinero',
          message: 'Al confirmar, las ventas con efectivo enviarán una orden de apertura a esta impresora. Verifica primero que el cajón y el cable sean compatibles con la salida RJ12 y que la cola de Windows corresponda a la impresora correcta.',
          confirmText: 'Habilitar cajón',
        }, this.destroyRef);
      } finally { this.confirming = false; }
      if (!approved || this.destroyRef.destroyed) return;
    }
    const current = this.profiles().find(item => item.id === profile.id);
    if (!current || current.version !== profile.version || !this.session.has('CONFIGURATION.MANAGE')) {
      this.formError.set('El perfil cambió. Actualiza la lista antes de guardarlo.'); return;
    }
    this.drawerSaving.set(true); this.formError.set(''); this.notice.set('');
    try {
      await this.api.updatePrintProfileDrawer(profile.id, { enabled, pin, pulseOnMs, pulseOffMs, expectedVersion: profile.version });
      this.drawerEditing.set(null);
      this.notice.set(enabled ? 'Configuración guardada. La orden enviada a Windows no confirma que el cajón se abrió.' : 'Apertura de cajón desactivada para este perfil.');
      await this.reload();
    } catch (e) { this.formError.set(errorText(e)); }
    finally { this.drawerSaving.set(false); }
  }

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
    if (this.saving() || this.deleting() || this.confirming) return;
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
    if (!this.session.has('CONFIGURATION.MANAGE') || this.deleting() || this.saving() || this.confirming) return;
    const snapshot = { ...profile };
    this.confirming = true;
    let approved: boolean;
    try {
      approved = await this.confirmations.confirm({ title: 'Eliminar perfil de impresora',
        message: `¿Eliminar el perfil «${snapshot.name}»?${snapshot.isDefault ? ' Es el predeterminado: mientras no haya otro, no se imprimirá desde el sistema.' : ''}`,
        confirmText: 'Eliminar perfil', danger: true }, this.destroyRef);
    } finally { this.confirming = false; }
    if (!approved || this.destroyRef.destroyed || this.deleting() || this.saving() ||
      !this.session.has('CONFIGURATION.MANAGE') || profile.id !== snapshot.id || profile.isDefault !== snapshot.isDefault) return;
    this.deleting.set(true); this.error.set(''); this.notice.set('');
    try {
      const result = await this.api.deletePrintProfile(snapshot.id);
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
