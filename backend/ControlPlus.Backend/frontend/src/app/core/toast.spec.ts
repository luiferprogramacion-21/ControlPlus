import { TestBed } from '@angular/core/testing';
import { ERROR_TOAST_MS, statusKind, toastDuration, toastSignal, ToastService } from './toast';

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

  it('los avisos de impresión son error solo si hablan de un fallo', () => {
    expect(statusKind('Ticket: Impreso. La impresora lo recibió.')).toBe('success');
    expect(statusKind('No se sabe si salió: mira la impresora antes de pedir otra copia.')).toBe('error');
    expect(statusKind('No se pudo completar la operación.')).toBe('error');
  });
});

describe('Qué sigue al terminar', () => {
  it('ofrece el siguiente paso solo con el permiso de ese paso', async () => {
    const { nextStep } = await import('./toast');
    expect(nextStep('Turno abierto.', p => p === 'SALES.CREATE')?.label).toBe('Ir a vender');
    expect(nextStep('Turno abierto.', () => false)).toBeUndefined();
    expect(nextStep('Turno cerrado.', () => false)?.link).toBe('/operacion/inicio');
    expect(nextStep('Producto guardado. N.º de producto: PAP-000004.', p => p === 'PRODUCTS.LABELS_MANAGE')?.label).toBe('Imprimir etiqueta');
    expect(nextStep('No se pudo guardar.', () => true)).toBeUndefined();
  });
});
