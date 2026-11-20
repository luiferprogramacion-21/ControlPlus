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
/** Tamaño libre (19 de noviembre de 2026): de 80 % a 200 %, en pasos de 5 %. */
export const MIN_DISPLAY_SCALE = 0.8;
export const MAX_DISPLAY_SCALE = 2;
export const DISPLAY_SCALE_STEP = 0.05;
export const DISPLAY_SCALE_STORAGE_KEY = 'controlplus.displayScale';

function isAllowed(value: number): boolean { return Number.isFinite(value) && value >= MIN_DISPLAY_SCALE - 1e-9 && value <= MAX_DISPLAY_SCALE + 1e-9; }
/** Redondea al paso de 5 % para que sumar y restar no acumule decimales. */
export function normalizeScale(value: number): number {
  const stepped = Math.round(value / DISPLAY_SCALE_STEP) * DISPLAY_SCALE_STEP;
  return Math.round(Math.min(MAX_DISPLAY_SCALE, Math.max(MIN_DISPLAY_SCALE, stepped)) * 100) / 100;
}

export function readStoredScale(): number {
  try {
    const stored = Number(localStorage.getItem(DISPLAY_SCALE_STORAGE_KEY));
    return stored && isAllowed(stored) ? normalizeScale(stored) : DEFAULT_DISPLAY_SCALE;
  } catch { return DEFAULT_DISPLAY_SCALE; }
}

export function applyDisplayScale(value: number, root: HTMLElement = document.documentElement): void {
  root.style.setProperty('--cp-zoom', String(value));
}

@Injectable({ providedIn: 'root' })
export class DisplayScaleService {
  readonly options = DISPLAY_SCALES;
  readonly scale = signal(readStoredScale());

  /** Porcentaje para mostrar, por ejemplo «125 %». */
  percent(): string { return `${Math.round(this.scale() * 100)} %`; }
  increase(): void { this.set(normalizeScale(this.scale() + DISPLAY_SCALE_STEP)); }
  decrease(): void { this.set(normalizeScale(this.scale() - DISPLAY_SCALE_STEP)); }
  reset(): void { this.set(DEFAULT_DISPLAY_SCALE); }

  set(value: number): void {
    if (!isAllowed(value)) return;
    value = normalizeScale(value);
    this.scale.set(value);
    applyDisplayScale(value);
    try { localStorage.setItem(DISPLAY_SCALE_STORAGE_KEY, String(value)); } catch { /* Sin almacenamiento: dura hasta recargar. */ }
  }
}
