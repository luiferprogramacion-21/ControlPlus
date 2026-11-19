import { TestBed } from '@angular/core/testing';
import { applyDisplayScale, DEFAULT_DISPLAY_SCALE, DISPLAY_SCALE_STORAGE_KEY, DisplayScaleService, readStoredScale } from './display-scale';

describe('Tamaño de la pantalla', () => {
  beforeEach(() => { localStorage.removeItem(DISPLAY_SCALE_STORAGE_KEY); document.documentElement.style.removeProperty('--cp-zoom'); });
  afterEach(() => { localStorage.removeItem(DISPLAY_SCALE_STORAGE_KEY); document.documentElement.style.removeProperty('--cp-zoom'); });

  it('usa Grande (125 %) si el equipo no ha elegido o guardó un valor no permitido', () => {
    expect(readStoredScale()).toBe(DEFAULT_DISPLAY_SCALE);
    localStorage.setItem(DISPLAY_SCALE_STORAGE_KEY, '3');
    expect(readStoredScale()).toBe(1.25);
  });

  it('aplica y recuerda el tamaño elegido', () => {
    const service = TestBed.inject(DisplayScaleService);
    service.set(1.5);
    expect(service.scale()).toBe(1.5);
    expect(document.documentElement.style.getPropertyValue('--cp-zoom')).toBe('1.5');
    expect(localStorage.getItem(DISPLAY_SCALE_STORAGE_KEY)).toBe('1.5');
    expect(readStoredScale()).toBe(1.5);
  });

  it('ignora tamaños fuera de la lista', () => {
    const service = TestBed.inject(DisplayScaleService);
    applyDisplayScale(1);
    service.set(0.5);
    expect(service.scale()).toBe(DEFAULT_DISPLAY_SCALE);
    expect(document.documentElement.style.getPropertyValue('--cp-zoom')).toBe('1');
  });
});
