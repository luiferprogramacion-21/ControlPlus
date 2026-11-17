import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type BackupDestinationType = 'LOCAL_FISICO_SEPARADO' | 'EXTERNO_CIFRADO';

export interface BackupExecution {
  id: string; startedAt: string; finishedAt: string | null; trigger: string; result: string;
  sizeBytes: number | null; verified: boolean; error: string | null;
}

export interface BackupCopy {
  id: string; executionId: string; destinationType: BackupDestinationType; completedAt: string | null;
  state: string; encrypted: boolean; verified: boolean; error: string | null;
}

export interface BackupDestination {
  type: BackupDestinationType; name: string; path: string | null; active: boolean; encrypted: boolean;
  passphraseConfigured: boolean; dailyRetention: number; weeklyRetention: number; monthlyRetention: number;
  freeBytes: number | null; totalBytes: number | null; lastCopy: BackupCopy | null; version: number | null;
}

export interface BackupEvent { at: string; kind: 'RESPALDO' | 'COPIA'; description: string; result: string; }

/** Phase 4 backups: every 30 minutes, after each cash close and on demand. Only the Administrator sees this. */
export interface BackupStatus {
  serviceEnabled: boolean; intervalMinutes: number;
  lastBackup: BackupExecution | null; lastSuccessfulBackup: BackupExecution | null; lastExternalCopy: BackupCopy | null;
  destinations: BackupDestination[]; recentEvents: BackupEvent[];
}

export interface RestorableCopy { copyId: string; backupAt: string; destinationType: BackupDestinationType; sizeBytes: number | null; verified: boolean; available: boolean; }
export interface RestoreTest {
  id: string; copyId: string; startedAt: string; finishedAt: string | null; result: string;
  rpoObservedMinutes: number | null; rtoObservedMinutes: number | null; integrityVerified: boolean; applicationVerified: boolean; error: string | null;
}
/** Phase 4 goals of the establishment, last restore test and the copies that can be tested. */
export interface RestoreOverview {
  rpoMinutes: number; rtoMinutes: number; restoreTestDays: number; lastTest: RestoreTest | null; nextTestDue: string | null; copies: RestorableCopy[];
}

/** Perfil de impresora activo de la terminal (GET /api/printing/status). */
export interface PrinterStatus { hasActiveProfile: boolean; profileName: string | null; columns: number | null; }
export type PrintJobState = 'PENDIENTE' | 'IMPRIMIENDO' | 'IMPRESO' | 'ERROR';
export interface PrintQueuePage { items: { status: PrintJobState }[]; totalCount: number; }
/** Lo que Soporte muestra de un componente: texto corto, detalle y tono del distintivo. */
export interface ComponentDiagnosis { label: string; detail: string; tone: 'success' | 'warning' | 'error' | 'muted'; }

/**
 * Impresora: solo se sabe si hay un perfil configurado. Que la impresora responda lo confirma la prueba física,
 * así que nunca se muestra «Disponible».
 */
export function diagnosePrinter(status: PrinterStatus | null): ComponentDiagnosis {
  if (!status) return { label: 'Sin consultar', detail: 'No se pudo leer la configuración de impresión.', tone: 'muted' };
  if (!status.hasActiveProfile) return { label: 'Sin configurar', detail: 'No hay un perfil de impresora activo (Configuración → Impresión).', tone: 'warning' };
  return { label: 'Configurada', detail: `Perfil «${status.profileName ?? 'sin nombre'}»${status.columns ? `, ${status.columns} columnas` : ''}.`, tone: 'success' };
}

/** Cola: estado de los trabajos más recientes (la consulta no filtra por estado). */
export function diagnoseQueue(page: PrintQueuePage | null): ComponentDiagnosis {
  if (!page) return { label: 'Sin consultar', detail: 'No se pudo leer la cola de impresión.', tone: 'muted' };
  if (!page.items.length) return { label: 'Sin trabajos', detail: 'Todavía no se ha enviado nada a imprimir.', tone: 'muted' };
  const errors = page.items.filter(job => job.status === 'ERROR').length;
  const pending = page.items.filter(job => job.status === 'PENDIENTE' || job.status === 'IMPRIMIENDO').length;
  const detail = `Últimos ${page.items.length} de ${page.totalCount} trabajos.`;
  if (errors) return { label: `${errors} con error`, detail: `${detail} Revíselos en Configuración → Impresión.`, tone: 'error' };
  if (pending) return { label: `${pending} ${pending === 1 ? 'pendiente' : 'pendientes'}`, detail, tone: 'warning' };
  return { label: 'Al día', detail, tone: 'success' };
}

@Injectable({ providedIn: 'root' })
export class SupportApi {
  private readonly http = inject(HttpClient);

  status(): Promise<BackupStatus> { return firstValueFrom(this.http.get<BackupStatus>('/api/backups/status')); }
  run(): Promise<unknown> { return firstValueFrom(this.http.post('/api/backups/run', null)); }
  restoreOverview(): Promise<RestoreOverview> { return firstValueFrom(this.http.get<RestoreOverview>('/api/backups/restore')); }
  /** Restores the copy into a temporary database that is dropped afterwards; the real database is never touched. */
  restoreTest(copyId: string): Promise<RestoreTest> { return firstValueFrom(this.http.post<RestoreTest>(`/api/backups/copies/${copyId}/restore-test`, null)); }
  updateDestination(type: BackupDestinationType, path: string, active: boolean, expectedVersion: number | null): Promise<BackupDestination> {
    return firstValueFrom(this.http.put<BackupDestination>(`/api/backups/destinations/${type}`, { path, active, expectedVersion }));
  }
  printerStatus(): Promise<PrinterStatus> { return firstValueFrom(this.http.get<PrinterStatus>('/api/printing/status')); }
  printQueue(): Promise<PrintQueuePage> { return firstValueFrom(this.http.get<PrintQueuePage>('/api/printing/jobs', { params: { page: 1, pageSize: 20 } })); }
  /** True when the endpoint answers 200; any error means not available. */
  async healthy(path: '/api/health' | '/api/health/database'): Promise<boolean> {
    try { await firstValueFrom(this.http.get(path, { observe: 'response', responseType: 'text' })); return true; }
    catch { return false; }
  }
}
