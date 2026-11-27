import { Injectable, signal } from '@angular/core';

/** Quien pasó su carné en el turno compartido, con sus permisos. */
export interface ActiveOperator { name: string; role: string | null; permissions: readonly string[]; }

/** Lo que trae el estado de la caja sobre la sesión de operador (ver `CashState`). */
export interface OperatorStateSource {
  requiresOperatorSession?: boolean;
  ownOperatorSession?: unknown;
  activeOperatorName?: string | null;
  activeOperatorRole?: string | null;
  activeOperatorPermissions?: readonly string[] | null;
}

const ROLE_LABELS: Record<string, string> = { CAJERO: 'Cajero', SUPERVISOR: 'Supervisor', ADMINISTRADOR: 'Administrador' };

/**
 * Opción B del turno compartido (pruebas del usuario del 26 de noviembre de 2026): mientras alguien tiene su carné activo
 * en el computador, el menú apaga lo que esa persona no puede usar. Se actualiza cada vez que se consulta el estado de la
 * caja; el servidor sigue decidiendo cada operación con los permisos del dueño del carné.
 */
@Injectable({ providedIn: 'root' })
export class OperatorPresence {
  readonly active = signal<ActiveOperator | null>(null);

  update(state: OperatorStateSource | null | undefined): void {
    const permissions = state?.activeOperatorPermissions;
    this.active.set(state?.requiresOperatorSession && state.ownOperatorSession && permissions
      ? { name: state.activeOperatorName?.trim() || 'El operador', role: state.activeOperatorRole ?? null, permissions }
      : null);
  }

  /** Mismo contrato que `SessionService.has`, pero con los permisos de quien pasó el carné. */
  has(code: string): boolean { return this.active()?.permissions.includes(code) ?? false; }

  roleLabel(): string { const role = this.active()?.role; return role ? ROLE_LABELS[role] ?? role : ''; }
}
