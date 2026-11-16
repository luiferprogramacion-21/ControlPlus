import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorText } from '../../core/api';
import { BackupDestination, BackupDestinationType, BackupStatus, SupportApi } from './support-api';

interface DestinationDraft { path: string; active: boolean; }

@Component({
  selector: 'cp-support',
  imports: [FormsModule, RouterLink],
  templateUrl: './support.html',
  styleUrl: './support.scss',
})
export class Support implements OnInit {
  private readonly api = inject(SupportApi);
  readonly status = signal<BackupStatus | null>(null);
  readonly apiUp = signal<boolean | null>(null);
  readonly databaseUp = signal<boolean | null>(null);
  readonly loading = signal(false);
  readonly running = signal(false);
  readonly saving = signal<BackupDestinationType | null>(null);
  readonly error = signal('');
  readonly notice = signal('');
  /** Error or confirmation shown inside the destination card, where the Administrator is looking. */
  readonly destinationMessage = signal<{ type: BackupDestinationType; text: string; ok: boolean } | null>(null);
  drafts: Record<BackupDestinationType, DestinationDraft> = {
    LOCAL_FISICO_SEPARADO: { path: '', active: true },
    EXTERNO_CIFRADO: { path: '', active: true },
  };

  /** Free space of the separate local disk, the one the mockup's «Espacio disponible» card refers to. */
  readonly localDestination = computed(() => this.status()?.destinations.find(x => x.type === 'LOCAL_FISICO_SEPARADO') ?? null);

  ngOnInit(): void { void this.refresh(); }

  async refresh(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true); this.error.set('');
    const [api, database] = await Promise.all([this.api.healthy('/api/health'), this.api.healthy('/api/health/database')]);
    this.apiUp.set(api); this.databaseUp.set(database);
    try {
      const status = await this.api.status();
      this.status.set(status);
      for (const destination of status.destinations)
        this.drafts[destination.type] = { path: destination.path ?? '', active: destination.path ? destination.active : true };
    } catch (e) { this.status.set(null); this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  async runNow(): Promise<void> {
    if (this.running()) return;
    this.running.set(true); this.error.set(''); this.notice.set('');
    try {
      await this.api.run();
      this.notice.set('Respaldo solicitado. Se ejecuta en segundo plano; pulse «Actualizar diagnóstico» en un momento para ver el resultado.');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.running.set(false); }
  }

  async saveDestination(destination: BackupDestination): Promise<void> {
    const draft = this.drafts[destination.type];
    if (!draft.path.trim()) { this.destinationMessage.set({ type: destination.type, text: 'Indique la carpeta del destino.', ok: false }); return; }
    this.saving.set(destination.type); this.destinationMessage.set(null);
    try {
      await this.api.updateDestination(destination.type, draft.path.trim(), draft.active, destination.version);
      this.saving.set(null);
      await this.refresh();
      this.destinationMessage.set({ type: destination.type, text: 'Carpeta guardada. Se usará desde el próximo respaldo.', ok: true });
    } catch (e) { this.destinationMessage.set({ type: destination.type, text: errorText(e), ok: false }); this.saving.set(null); }
  }

  time(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Intl.DateTimeFormat('es-CO', { hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(iso));
  }

  dateTime(iso: string | null | undefined): string {
    if (!iso) return '—';
    const date = new Date(iso);
    return `${new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(date)} · ${this.time(iso)}`;
  }

  size(bytes: number | null | undefined): string {
    if (bytes === null || bytes === undefined) return '—';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let value = bytes; let unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return `${new Intl.NumberFormat('es-CO', { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`;
  }

  freePercent(destination: BackupDestination | null): string {
    if (!destination?.freeBytes || !destination.totalBytes) return '';
    return `${Math.round((destination.freeBytes / destination.totalBytes) * 100)} % libre`;
  }

  resultLabel(result: string): string {
    if (result === 'EXITOSO' || result === 'COMPLETADA') return 'Correcto';
    if (result === 'FALLIDO' || result === 'FALLIDA') return 'Falló';
    return 'En proceso';
  }

  resultClass(result: string): string {
    if (result === 'EXITOSO' || result === 'COMPLETADA') return 'badge--success';
    if (result === 'FALLIDO' || result === 'FALLIDA') return 'badge--error';
    return 'badge--warning';
  }

  /** Text under «Copia externa», as in the mockup («Sin pendientes» when the last copy completed). */
  externalHint(status: BackupStatus): string {
    const external = status.destinations.find(x => x.type === 'EXTERNO_CIFRADO');
    if (!external?.path || !external.active) return 'Sin configurar';
    if (!status.lastExternalCopy) return 'Aún sin copias';
    return status.lastExternalCopy.state === 'COMPLETADA' ? 'Sin pendientes' : (status.lastExternalCopy.error ?? 'Falló el último envío');
  }
}
