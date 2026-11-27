import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Establishment {
  commercialName: string;
  identification: string | null;
  phone: string | null;
  address: string | null;
  receiptHeader: string | null;
  receiptFooter: string | null;
  version: number;
}

export interface EstablishmentUpdate {
  commercialName: string;
  identification: string | null;
  phone: string | null;
  address: string | null;
  receiptHeader: string | null;
  receiptFooter: string | null;
  expectedVersion: number;
}

/** Shared, read-through store for the single establishment used by the local V1 installation. */
@Injectable({ providedIn: 'root' })
export class EstablishmentService {
  private readonly http = inject(HttpClient);
  private readonly state = signal<Establishment | null>(null);
  private pending: Promise<Establishment> | null = null;

  readonly value = this.state.asReadonly();
  /** Nombre de Configuración → Establecimiento; si aún no se ha cargado, ControlPlus. */
  readonly name = computed(() => this.state()?.commercialName || 'ControlPlus');

  load(force = false): Promise<Establishment> {
    if (!force && this.state()) return Promise.resolve(this.state()!);
    if (!force && this.pending) return this.pending;
    const request = firstValueFrom(this.http.get<Establishment>('/api/establishment'));
    this.pending = request;
    return request.then(value => { this.state.set(value); return value; })
      .finally(() => { if (this.pending === request) this.pending = null; });
  }

  update(input: EstablishmentUpdate): Promise<Establishment> {
    return firstValueFrom(this.http.put<Establishment>('/api/establishment', input))
      .then(value => { this.state.set(value); return value; });
  }
}
