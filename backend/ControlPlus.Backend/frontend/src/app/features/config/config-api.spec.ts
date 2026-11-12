import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfigApi, NUMBERING_KINDS, REASON_GROUPS } from './config-api';

describe('ConfigApi', () => {
  let api: ConfigApi; let http: HttpTestingController;
  const kind = (key: string) => NUMBERING_KINDS.find(k => k.key === key)!;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ConfigApi); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('cada tipo de documento usa su ruta y los prefijos fijos son los aprobados', () => {
    expect(NUMBERING_KINDS.map(k => k.path)).toEqual(['sales', 'credit-payments', 'layaways', 'layaway-payments', 'purchases', 'sale-changes']);
    expect(NUMBERING_KINDS.filter(k => k.fixedPrefix).map(k => k.defaultPrefix)).toEqual(['ABO', 'APT', 'PAP', 'CMP', 'CAM']);
    expect(kind('sales').fixedPrefix).toBe(false);
  });

  it('un consecutivo sin configurar (404) se devuelve como null', async () => {
    const task = api.numbering(kind('sale-changes'));
    http.expectOne('/api/document-numbering/sale-changes').flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    expect(await task).toBeNull();
  });

  it('otros errores de la consulta se propagan', async () => {
    const task = api.numbering(kind('sales')); const assertion = expect(task).rejects.toBeTruthy();
    http.expectOne('/api/document-numbering/sales').flush({}, { status: 403, statusText: 'Forbidden' });
    await assertion;
  });

  it('crea y corrige un consecutivo con el cuerpo del contrato', async () => {
    const created = api.createNumbering(kind('sales'), 'VTA', 1);
    const post = http.expectOne('/api/document-numbering/sales');
    expect([post.request.method, post.request.body]).toEqual(['POST', { prefix: 'VTA', firstNumber: 1 }]); post.flush({});
    await created;
    const updated = api.updateNumbering(kind('purchases'), 'CMP', 5, 3);
    const put = http.expectOne('/api/document-numbering/purchases');
    expect([put.request.method, put.request.body]).toEqual(['PUT', { prefix: 'CMP', firstNumber: 5, version: 3 }]); put.flush({});
    await updated;
  });

  it('los créditos leen y guardan el plazo predeterminado (o nulo) con la versión esperada', async () => {
    const read = api.creditSettings(); http.expectOne('/api/credits/settings').flush({ defaultDays: 30, version: 1 }); expect((await read).defaultDays).toBe(30);
    const task = api.updateCreditSettings(45, 1);
    const put = http.expectOne('/api/credits/settings');
    expect([put.request.method, put.request.body]).toEqual(['PUT', { defaultDays: 45, expectedVersion: 1 }]); put.flush({ defaultDays: 45, version: 2 });
    await task;
    const none = api.updateCreditSettings(null, 2); const putNone = http.expectOne('/api/credits/settings');
    expect(putNone.request.body).toEqual({ defaultDays: null, expectedVersion: 2 }); putNone.flush({ defaultDays: null, version: 3 }); await none;
  });

  it('la política de cambios envía los días y la versión esperada', async () => {
    const task = api.updateChangePolicy(20, 4);
    const put = http.expectOne('/api/sales/change-policy');
    expect([put.request.method, put.request.body]).toEqual(['PUT', { days: 20, expectedVersion: 4 }]); put.flush({ days: 20, version: 5 });
    expect(await task).toEqual({ days: 20, version: 5 });
  });

  it('los apartados envían porcentaje, plazo (o nulo) y versión', async () => {
    const task = api.updateLayawaySettings(25.5, null, false, 2);
    const put = http.expectOne('/api/layaways/settings');
    expect([put.request.method, put.request.body]).toEqual(['PUT', { minimumPercentage: 25.5, defaultDays: null, requireMinimumInitial: false, expectedVersion: 2 }]); put.flush({ minimumPercentage: 25.5, defaultDays: null, requireMinimumInitial: false, version: 3 });
    await task;
  });

  it('consulta e inicializa los motivos de cada grupo', async () => {
    for (const group of REASON_GROUPS) {
      const list = api.reasons(group); http.expectOne(group.list).flush([]); await list;
      const init = api.initializeReasons(group);
      const post = http.expectOne(group.initialize); expect(post.request.method).toBe('POST'); post.flush([]); await init;
    }
  });
});
