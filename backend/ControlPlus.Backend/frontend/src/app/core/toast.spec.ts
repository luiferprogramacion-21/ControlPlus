import { TestBed } from '@angular/core/testing';
import { ERROR_TOAST_MS, toastDuration, toastSignal, ToastService } from './toast';

describe('mensajes emergentes', () => {
  beforeEach(() => { vi.useFakeTimers(); TestBed.configureTestingModule({}); });
  afterEach(() => vi.useRealTimers());

  it('muestra el aviso y lo quita solo; los errores duran más', () => {
    const toasts = TestBed.inject(ToastService);
    toasts.show('Producto guardado.');
    toasts.show('No se pudo guardar.', 'error');
    expect(toasts.toasts().map(t => t.kind)).toEqual(['success', 'error']);
    vi.advanceTimersByTime(toastDuration('Producto guardado.', 'success'));
    expect(toasts.toasts().map(t => t.message)).toEqual(['No se pudo guardar.']);
    vi.advanceTimersByTime(ERROR_TOAST_MS);
    expect(toasts.toasts()).toEqual([]);
  });

  it('no apila el mismo mensaje, ignora vacíos y se puede cerrar antes', () => {
    const toasts = TestBed.inject(ToastService);
    toasts.show('Turno abierto.'); toasts.show('Turno abierto.'); toasts.show('   ');
    expect(toasts.toasts()).toHaveLength(1);
    toasts.dismiss(toasts.toasts()[0].id);
    expect(toasts.toasts()).toEqual([]);
  });

  it('la duración depende del largo y nunca pasa de 8 segundos en confirmaciones', () => {
    expect(toastDuration('Listo.', 'success')).toBe(4000);
    expect(toastDuration('x'.repeat(500), 'success')).toBe(8000);
    expect(toastDuration('x', 'error')).toBe(ERROR_TOAST_MS);
  });

  it('la señal de aviso de una pantalla lo muestra al cambiar', () => {
    const toasts = TestBed.inject(ToastService);
    const notice = TestBed.runInInjectionContext(() => toastSignal('success'));
    notice.set('Cliente guardado.'); TestBed.tick();
    expect(toasts.toasts().map(t => t.message)).toEqual(['Cliente guardado.']);
    expect(notice()).toBe('Cliente guardado.');
  });
});
