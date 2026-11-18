import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { EstablishmentService } from '../../core/establishment';
import { EstablishmentPanel } from './establishment-panel';

describe('Datos del establecimiento', () => {
  const saved = { commercialName: 'Baratísimo', identification: '9001', phone: null, address: 'Ocaña', receiptHeader: null, receiptFooter: 'Gracias', version: 2 };
  const load = vi.fn(); const update = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks(); load.mockResolvedValue(saved);
    TestBed.configureTestingModule({ providers: [{ provide: EstablishmentService, useValue: { load, update } }] });
  });

  async function open() {
    const fixture = TestBed.createComponent(EstablishmentPanel); fixture.detectChanges(); await fixture.whenStable();
    return { fixture, component: fixture.componentInstance };
  }

  it('sigue la maqueta con los seis campos persistidos y sin inventar correo', async () => {
    const { fixture } = await open(); fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    for (const label of ['Nombre comercial', 'NIT o documento', 'Teléfono', 'Dirección', 'Encabezado del comprobante', 'Pie del comprobante']) expect(text).toContain(label);
    expect(fixture.nativeElement.querySelector('input[type="email"]')).toBeNull();
  });

  it('guarda los opcionales vacíos como nulos y adopta la nueva versión', async () => {
    const { component } = await open();
    update.mockResolvedValue({ ...saved, commercialName: 'Nuevo nombre', phone: '555', version: 3 });
    component.commercialName = ' Nuevo nombre '; component.phone = ' 555 '; component.address = '';
    await component.save();
    expect(update).toHaveBeenCalledWith(expect.objectContaining({ commercialName: 'Nuevo nombre', phone: '555', address: null, expectedVersion: 2 }));
    expect(component.current()?.version).toBe(3); expect(component.notice()).toContain('Datos guardados');
  });

  it('no envía nombre ni NIT vacíos', async () => {
    const { component } = await open(); component.commercialName = ' ';
    await component.save(); expect(update).not.toHaveBeenCalled(); expect(component.error()).toContain('nombre comercial');
  });
});
