import { Component, effect, inject, Injectable, signal, untracked, WritableSignal } from '@angular/core';

/**
 * Mensajes emergentes (decisión del 18 de noviembre de 2026): confirmaciones y errores aparecen
 * en una esquina y se quitan solos. Las confirmaciones duran de 4 a 8 segundos según su largo;
 * los errores, 10 segundos. Todos se pueden cerrar antes.
 */
export type ToastKind = 'success' | 'error';
export interface Toast { id: number; message: string; kind: ToastKind; }

export const ERROR_TOAST_MS = 10_000;
export function toastDuration(message: string, kind: ToastKind): number {
  return kind === 'error' ? ERROR_TOAST_MS : Math.min(8_000, Math.max(4_000, 3_000 + message.length * 40));
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private next = 1;

  show(message: string, kind: ToastKind = 'success'): void {
    const text = message?.trim();
    if (!text) return;
    const id = this.next++;
    // El mismo mensaje no se apila: se reemplaza por el nuevo. Como máximo se ven cuatro.
    this.toasts.update(list => [...list.filter(toast => toast.message !== text), { id, message: text, kind }].slice(-4));
    setTimeout(() => this.dismiss(id), toastDuration(text, kind));
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
  /error|no se pud|no se puede|incierto|falló|fallo|sin conexión|no tienes|rechaz/i.test(message) ? 'error' : 'success';

@Component({
  selector: 'cp-toast-host',
  template: `
    <div class="toast-host" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class.toast--error]="toast.kind === 'error'" [attr.role]="toast.kind === 'error' ? 'alert' : 'status'">
          <span class="toast__icon" aria-hidden="true">{{ toast.kind === 'error' ? '!' : '✓' }}</span>
          <span class="toast__text">{{ toast.message }}</span>
          <button class="toast__close" type="button" aria-label="Cerrar aviso" (click)="toasts.dismiss(toast.id)">×</button>
        </div>
      }
    </div>
  `,
})
export class ToastHost {
  readonly toasts = inject(ToastService);
}
