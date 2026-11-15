import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session.service';
import { Credit } from './credits-api';
import { Credits } from './credits';

const credit = { id: 'credit-id', saleNumber: 'VTA-1', customerName: 'Ana', adjustedAmount: 10000, balance: 5000, state: 'PENDIENTE', version: 4 } as Credit;

describe('Credits', () => {
  let component: Credits; let fixture: ComponentFixture<Credits>; let http: HttpTestingController;
  let onRequest: ((url: string) => void) | null;
  beforeEach(async () => {
    onRequest = null;
    await TestBed.configureTestingModule({ imports: [Credits], providers: [provideHttpClient(withInterceptors([(request, next) => { onRequest?.(request.url); return next(request); }])), provideHttpClientTesting(), { provide: SessionService, useValue: { has: (permission: string) => permission === 'CREDIT.MANAGE' } }] }).compileComponents();
    fixture = TestBed.createComponent(Credits); component = fixture.componentInstance; http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('impide abonos superiores al saldo y medios repetidos antes de enviar', () => {
    component.selected.set(credit);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 6000, receivedAmount: 6000, reference: '' }];
    expect(component.validation()).toContain('supera el saldo');
    component.drafts = [{ methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'A' }, { methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'B' }];
    expect(component.validation()).toContain('una sola vez');
  });
  it('muestra el detalle en una ventana modal y no debajo de la lista', () => {
    component.selected.set(credit); component.detailOpen.set(true); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/credits').flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector('[role="dialog"][aria-labelledby="credit-detail-title"]');
    expect(modal).toBeTruthy(); expect(modal.textContent).toContain('VTA-1');
    expect(fixture.nativeElement.querySelector('section.credit-detail')).toBeNull();
    modal.querySelector('.module-form-sheet__backdrop').click(); fixture.detectChanges();
    expect(component.detailOpen()).toBe(false);
  });
  it('bloquea un segundo envío mientras espera respuesta y usa saldo del servidor', async () => {
    let detailRequested!: () => void;
    let listRequested!: () => void;
    let postCount = 0;
    const detailStarted = new Promise<void>(resolve => { detailRequested = resolve; });
    const listStarted = new Promise<void>(resolve => { listRequested = resolve; });
    onRequest = url => {
      if (url === '/api/credits/credit-id/payments') postCount++;
      if (url === '/api/credits/credit-id') detailRequested();
      if (url === '/api/credits') listRequested();
    };
    component.selected.set(credit);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 6000, reference: '' }];
    const first = component.save(); const second = component.save();
    const request = http.expectOne('/api/credits/credit-id/payments');
    expect(request.request.body.expectedVersion).toBe(4);
    expect(postCount).toBe(1);
    request.flush({ creditId: 'credit-id', balance: 0, state: 'PAGADO', version: 5, correlationId: 'correlation', payments: [] });
    await detailStarted;
    const detail = http.expectOne('/api/credits/credit-id'); detail.flush({ ...credit, balance: 0, state: 'PAGADO', version: 5 });
    await listStarted;
    const list = http.expectOne(r => r.url === '/api/credits');
    expect(list.request.params.get('page')).toBe('1');
    expect(list.request.params.get('pageSize')).toBe('20');
    list.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    await Promise.all([first, second]);
    expect(postCount).toBe(1);
    expect(component.selected()?.balance).toBe(0);
    expect(component.result()?.items).toEqual([]);
  });
});
