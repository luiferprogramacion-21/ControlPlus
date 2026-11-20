import { Component, effect, inject, Injectable, signal, untracked, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionService } from './session.service';

/**
 * Mensajes emergentes (decisión del 18 de noviembre de 2026): confirmaciones y errores aparecen
 * en una esquina y se quitan solos. Las confirmaciones duran de 4 a 8 segundos según su largo;
 * los errores, 10 segundos. Todos se pueden cerrar antes.
 */
export type ToastKind = 'success' | 'error';
export interface ToastAction { label: string; link: string; }
export interface Toast { id: number; message: string; kind: ToastKind; action?: ToastAction; }

/**
 * Qué sigue al terminar (decisión del 19 de noviembre de 2026): las confirmaciones ofrecen el siguiente paso
 * habitual. Se decide aquí, por el texto de la confirmación, y solo si el usuario tiene el permiso de ese paso.
 */
export const NEXT_STEPS: { pattern: RegExp; action: ToastAction; permission?: string }[] = [
  { pattern: /^(Turno abierto|Sesión iniciada)\./, action: { label: 'Ir a vender', link: '/operacion/ventas' }, permission: 'SALES.CREATE' },
  { pattern: /^Turno cerrado\./, action: { label: 'Ir a Inicio', link: '/operacion/inicio' } },
  { pattern: /^Producto guardado\./, action: { label: 'Imprimir etiqueta', link: '/operacion/etiquetas' }, permission: 'PRODUCTS.LABELS_MANAGE' },
  { pattern: /^(Compra confirmada|Recepción registrada)\./, action: { label: 'Imprimir etiquetas', link: '/operacion/etiquetas' }, permission: 'PRODUCTS.LABELS_MANAGE' },
  { pattern: /^Cliente guardado\./, action: { label: 'Ir a vender', link: '/operacion/ventas' }, permission: 'SALES.CREATE' },
  { pattern: /^Proveedor guardado\./, action: { label: 'Hacerle un pedido', link: '/operacion/compras' }, permission: 'PURCHASE_ORDERS.MANAGE' },
  { pattern: /^(Cambio .+ registrado|Abono registrado|Ajuste registrado)\./, action: { label: 'Ir a Inicio', link: '/operacion/inicio' } },
];

export function nextStep(message: string, can: (permission: string) => boolean): ToastAction | undefined {
  return NEXT_STEPS.find(step => step.pattern.test(message) && (!step.permission || can(step.permission)))?.action;
}

export const ERROR_TOAST_MS = 10_000;
export function toastDuration(message: string, kind: ToastKind): number {
  return kind === 'error' ? ERROR_TOAST_MS : Math.min(8_000, Math.max(4_000, 3_000 + message.length * 40));
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private next = 1;
  private readonly session = inject(SessionService, { optional: true });

  show(message: string, kind: ToastKind = 'success'): void {
    const text = message?.trim();
    if (!text) return;
    const id = this.next++;
    const action = kind === 'success' && this.session ? nextStep(text, permission => this.session!.has(permission)) : undefined;
    // El mismo mensaje no se apila: se reemplaza por el nuevo. Como máximo se ven cuatro.
    this.toasts.update(list => [...list.filter(toast => toast.message !== text), { id, message: text, kind, ...(action ? { action } : {}) }].slice(-4));
    // Con un siguiente paso el aviso dura más, para alcanzar a pulsarlo.
    setTimeout(() => this.dismiss(id), action ? Math.max(8_000, toastDuration(text, kind)) : toastDuration(text, kind));
  }

  dismiss(id: number): void { this.toasts.update(list => list.filter(toast => toast.id !== id)); }
}

/**
 * Señal de aviso de una pantalla: guarda el texto como siempre y, cada vez que cambia a un texto
 * no vacío, lo muestra como mensaje emergente.
 */
export function toastSignal(kind: ToastKind | ((message: string) => ToastKind) = 'success'): WritableSignal<string> {
  const toasts = inject(ToastService);
  const value = signal('');
  effect(() => {
    const message = value();
    if (message) untracked(() => toasts.show(message, typeof kind === 'function' ? kind(message) : kind));
  });
  return value;
}

/** Para avisos de estado (impresión, reimpresión): son error si hablan de un fallo o de un resultado incierto. */
export const statusKind = (message: string): ToastKind =>
  /error|no se pud|no se puede|no se sabe|incierto|falló|fallo|sin conexión|no tienes|rechaz/i.test(message) ? 'error' : 'success';

@Component({
  selector: 'cp-toast-host',
  imports: [RouterLink],
  template: `
    <div class="toast-host" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class.toast--error]="toast.kind === 'error'" [attr.role]="toast.kind === 'error' ? 'alert' : 'status'">
          <span class="toast__icon" aria-hidden="true">{{ toast.kind === 'error' ? '!' : '✓' }}</span>
          <span class="toast__text">{{ toast.message }}@if (toast.action; as action) { <a class="toast__action" [routerLink]="action.link" (click)="toasts.dismiss(toast.id)">{{ action.label }} →</a> }</span>
          <button class="toast__close" type="button" aria-label="Cerrar aviso" (click)="toasts.dismiss(toast.id)">×</button>
        </div>
      }
    </div>
  `,
})
export class ToastHost {
  readonly toasts = inject(ToastService);
}
