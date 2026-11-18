import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { productLabelsGuard } from '../../core/guards';
import { ProductLabelsApi } from './product-labels-api';
import { ProductLabels } from './product-labels';

describe('Etiquetas de productos', () => {
  const profile = { id: 'profile', name: 'Térmica híbrida 01', printerType: 'HIBRIDA', widthMm: 50, heightMm: 30, resolutionDpi: 203, isDefault: true, labelTemplateName: 'Etiqueta mostrador' };
  const product = { id: 'product', internalCode: 'CAB-000001', barcode: '000123456789', name: 'Cable eléctrico 10 m', retailPrice: 48000 };
  const api = { profiles: vi.fn(), products: vi.fn(), queue: vi.fn() };
  const session = { has: vi.fn((code: string) => code === 'PRODUCTS.LABELS_MANAGE') };

  beforeEach(() => {
    vi.clearAllMocks(); api.profiles.mockResolvedValue([profile]); api.products.mockResolvedValue({ items: [product], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.queue.mockResolvedValue({ jobId: 'job', profileName: profile.name, labelCount: 4, status: 'PENDIENTE' });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ProductLabelsApi, useValue: api }, { provide: SessionService, useValue: session }] });
  });

  async function create() { const fixture = TestBed.createComponent(ProductLabels); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); return fixture; }

  it('elige el perfil predeterminado, busca por lector y muestra vista previa Code 128', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    expect(component.selectedProfileId()).toBe(profile.id);
    component.search = product.barcode; await component.find(); fixture.detectChanges();
    expect(component.selected()).toEqual([{ product, quantity: 1 }]);
    expect(fixture.nativeElement.textContent).toContain('Cable eléctrico 10 m');
    expect(fixture.nativeElement.querySelector('.barcode svg')).not.toBeNull();
  });

  it('ajusta cantidades y encola el lote con PRODUCTS.LABELS_MANAGE', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    component.add(product); component.changeQuantity(product.id, 4); await component.print();
    expect(api.queue).toHaveBeenCalledWith({ profileId: profile.id, lines: [{ productId: product.id, quantity: 4 }] }, expect.any(String));
    expect(component.notice()).toContain('4 etiquetas');
    expect(await TestBed.runInInjectionContext(() => productLabelsGuard({} as never, {} as never))).toBe(true);
  });

  it('reintenta con la misma clave cuando el resultado de la red es incierto', async () => {
    const fixture = await create(); const component = fixture.componentInstance; component.add(product);
    api.queue.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })).mockResolvedValueOnce({ jobId: 'job', profileName: profile.name, labelCount: 1 });
    await component.print(); await component.print();
    expect(api.queue).toHaveBeenCalledTimes(2);
    expect(api.queue.mock.calls[0][1]).toBe(api.queue.mock.calls[1][1]);
  });
});
