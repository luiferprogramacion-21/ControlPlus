import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { productLabelsGuard } from '../../core/guards';
import { ProductLabelsApi } from './product-labels-api';
import { ProductLabels } from './product-labels';
import { EstablishmentService } from '../../core/establishment';

describe('Etiquetas de productos', () => {
  const profile = { id: 'profile', name: 'Térmica híbrida 01', printerType: 'HIBRIDA', widthMm: 50, heightMm: 30, resolutionDpi: 203, isDefault: true, labelTemplateName: 'Etiqueta mostrador' };
  const product = { id: 'product', internalCode: 'CAB-000001', barcode: '000123456789', name: 'Cable eléctrico 10 m', retailPrice: 48000 };
  const receipt = { purchaseId: 'purchase', visibleNumber: 'CMP-000001', supplierName: 'Proveedor Centro', receivedAtUtc: '2026-11-18T15:00:00Z', pendingLabelCount: 6, lines: [{ product, receivedQuantity: 6, pendingQuantity: 6 }] };
  const job = { jobId: 'job', batchId: 'batch', profileId: profile.id, profileName: profile.name, labelCount: 6, status: 'ERROR', requestedAtUtc: '2026-11-18T15:01:00Z', sourcePurchaseId: 'purchase', error: 'No se pudo enviar a la impresora.' };
  const api = { profiles: vi.fn(), products: vi.fn(), queue: vi.fn(), receipts: vi.fn(), receipt: vi.fn(), jobs: vi.fn(), retry: vi.fn() };
  const session = { has: vi.fn((code: string) => code === 'PRODUCTS.LABELS_MANAGE') };

  beforeEach(() => {
    vi.clearAllMocks(); api.profiles.mockResolvedValue([profile]); api.products.mockResolvedValue({ items: [product], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.queue.mockResolvedValue({ jobId: 'job', profileName: profile.name, labelCount: 4, status: 'PENDIENTE' });
    api.receipts.mockResolvedValue({ items: [receipt], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }); api.receipt.mockResolvedValue(receipt);
    api.jobs.mockResolvedValue({ items: [job, { ...job, jobId: 'uncertain', status: 'IMPRIMIENDO', error: null }], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 }); api.retry.mockResolvedValue({ ...job, status: 'PENDIENTE', error: null });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ProductLabelsApi, useValue: api }, { provide: SessionService, useValue: session }, { provide: EstablishmentService, useValue: { name: () => 'Variedades del Centro', load: () => Promise.resolve() } }] });
  });

  async function create() { const fixture = TestBed.createComponent(ProductLabels); fixture.detectChanges(); await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges(); return fixture; }

  it('elige el perfil predeterminado, busca por lector y muestra vista previa Code 128', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    expect(component.selectedProfileId()).toBe(profile.id);
    component.search = product.barcode; await component.find(); fixture.detectChanges();
    expect(component.selected()).toEqual([{ product, quantity: 1 }]);
    expect(fixture.nativeElement.textContent).toContain('Cable eléctrico 10 m');
    const barcode = fixture.nativeElement.querySelector('.barcode img') as HTMLImageElement | null;
    expect(barcode).not.toBeNull();
    expect(barcode!.src).toContain('data:image/svg+xml');
    expect(fixture.nativeElement.querySelector('.product-label-header').textContent).toContain('Variedades del Centro');
    expect(fixture.nativeElement.querySelector('.product-label-header strong').textContent.replace(/\D/g, '')).toBe('48000');
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

  it('prepara solo las cantidades pendientes de una recepción y conserva su origen al imprimir', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    await component.showReceipts(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('CMP-000001');
    component.prepareReceipt(receipt); component.changeQuantity(product.id, 99);
    component.add({ ...product, id: 'unrelated' });
    expect(component.selected()).toEqual([{ product, quantity: 6 }]);
    component.changeQuantity(product.id, 4); await component.print();
    expect(api.queue).toHaveBeenCalledWith({ profileId: profile.id, sourcePurchaseId: receipt.purchaseId, lines: [{ productId: product.id, quantity: 4 }] }, expect.any(String));
    expect(component.selected()).toEqual([]); expect(component.sourceReceipt()).toBeNull();
  });

  it('divide una recepción grande en lotes sin superar 500 etiquetas', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    component.prepareReceipt({ ...receipt, pendingLabelCount: 800, lines: [{ product, receivedQuantity: 800, pendingQuantity: 800 }] });
    expect(component.totalLabels()).toBe(500);
    expect(component.notice()).toContain('El resto seguirá pendiente');
  });

  it('abre desde recepción con las cantidades que todavía faltan', async () => {
    TestBed.overrideProvider(ActivatedRoute, { useValue: { snapshot: { queryParamMap: convertToParamMap({ compra: receipt.purchaseId }) } } });
    const fixture = await create(); const component = fixture.componentInstance;
    expect(api.receipt).toHaveBeenCalledWith(receipt.purchaseId);
    expect(component.sourceReceipt()?.purchaseId).toBe(receipt.purchaseId);
    expect(component.totalLabels()).toBe(6);
  });

  it('desde Catálogo llega con la referencia del producto y lo agrega (maqueta 11)', async () => {
    TestBed.overrideProvider(ActivatedRoute, { useValue: { snapshot: { queryParamMap: convertToParamMap({ producto: product.internalCode }) } } });
    const fixture = await create(); const component = fixture.componentInstance;
    await vi.waitFor(() => expect(component.selected()).toEqual([{ product, quantity: 1 }]));
    expect(api.products).toHaveBeenCalledWith(product.internalCode, 1, 20);
  });

  it('reintenta solo el mismo lote fallido y no permite repetir uno de resultado incierto', async () => {
    const fixture = await create(); const component = fixture.componentInstance;
    await component.showJobs(); fixture.detectChanges();
    const buttons = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).filter(button => button.textContent?.includes('Reintentar lote'));
    expect(buttons).toHaveLength(1);
    expect(fixture.nativeElement.textContent).toContain('revisa la impresora');
    await component.retryJob({ ...job, status: 'IMPRIMIENDO' }); expect(api.retry).not.toHaveBeenCalled();
    await component.retryJob(job); expect(api.retry).toHaveBeenCalledWith(job.jobId); expect(api.queue).not.toHaveBeenCalled();
  });
});
