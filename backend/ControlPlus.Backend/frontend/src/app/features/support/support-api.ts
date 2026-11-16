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

@Injectable({ providedIn: 'root' })
export class SupportApi {
  private readonly http = inject(HttpClient);

  status(): Promise<BackupStatus> { return firstValueFrom(this.http.get<BackupStatus>('/api/backups/status')); }
  run(): Promise<unknown> { return firstValueFrom(this.http.post('/api/backups/run', null)); }
  updateDestination(type: BackupDestinationType, path: string, active: boolean, expectedVersion: number | null): Promise<BackupDestination> {
    return firstValueFrom(this.http.put<BackupDestination>(`/api/backups/destinations/${type}`, { path, active, expectedVersion }));
  }
  /** True when the endpoint answers 200; any error means not available. */
  async healthy(path: '/api/health' | '/api/health/database'): Promise<boolean> {
    try { await firstValueFrom(this.http.get(path, { observe: 'response', responseType: 'text' })); return true; }
    catch { return false; }
  }
}
