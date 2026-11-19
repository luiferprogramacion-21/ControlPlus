import { Injectable, signal } from '@angular/core';

/**
 * Tamaño de la pantalla (decisión del 19 de noviembre de 2026): todo ControlPlus —letras, botones,
 * campos y tablas— se agranda como la «Escala» de Windows. Por defecto 125 %; cada equipo recuerda
 * su elección. El tamaño se aplica con `--cp-zoom` sobre la aplicación y sobre las ventanas emergentes
 * (ver `styles.scss`); las alturas y anchos de pantalla se dividen entre ese valor para no desbordarse.
 */
export interface DisplayScaleOption { value: number; label: string; }

export const DISPLAY_SCALES: readonly DisplayScaleOption[] = [
  { value: 1, label: 'Normal (100 %)' },
  { value: 1.25, label: 'Grande (125 %)' },
  { value: 1.5, label: 'Muy grande (150 %)' },
];
export const DEFAULT_DISPLAY_SCALE = 1.25;
export const DISPLAY_SCALE_STORAGE_KEY = 'controlplus.displayScale';

function isAllowed(value: number): boolean { return DISPLAY_SCALES.some(option => option.value === value); }

export function readStoredScale(): number {
  try {
    const stored = Number(localStorage.getItem(DISPLAY_SCALE_STORAGE_KEY));
    return isAllowed(stored) ? stored : DEFAULT_DISPLAY_SCALE;
  } catch { return DEFAULT_DISPLAY_SCALE; }
}

export function applyDisplayScale(value: number, root: HTMLElement = document.documentElement): void {
  root.style.setProperty('--cp-zoom', String(value));
}

@Injectable({ providedIn: 'root' })
export class DisplayScaleService {
  readonly options = DISPLAY_SCALES;
  readonly scale = signal(readStoredScale());

  set(value: number): void {
    if (!isAllowed(value)) return;
    this.scale.set(value);
    applyDisplayScale(value);
    try { localStorage.setItem(DISPLAY_SCALE_STORAGE_KEY, String(value)); } catch { /* Sin almacenamiento: dura hasta recargar. */ }
  }
}
