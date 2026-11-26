import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session.service';
import { AdminApi } from './admin-api';
import { AUDIT_ACTION_GROUPS, auditActionLabel, auditActionModule } from './audit-actions';
import { AuditApi, AuditLog, AuditRecord, localDay } from './audit-log';

const record = (extra: Partial<AuditRecord> = {}): AuditRecord => ({
  id: 'e1', occurredAtUtc: '2026-11-16T15:30:00Z', actorUserId: 'u1', actorUserName: 'admin',
  actionCode: 'SaleCancelled', entityType: 'VENTA', entityId: 'v1', details: '{"total":5000,"motivo":"ERROR_COBRO"}',
  correlationId: null, authorizerUserId: 'u2', authorizerUserName: 'supervisor', reason: 'Error en el cobro', result: 'EXITOSO', ...extra,
});

describe('Auditoría', () => {
  const api = { records: vi.fn() };
  const admin = { users: vi.fn() };
  let permissions: string[];
  const session = { has: (code: string) => permissions.includes(code) };

  beforeEach(() => {
    vi.clearAllMocks();
    permissions = ['AUDIT.READ', 'USERS.READ'];
    api.records.mockResolvedValue({ items: [record(), record({ id: 'e2', actorUserId: null, actorUserName: null, actionCode: 'LoginFailed', result: 'RECHAZADO', details: null, authorizerUserName: null, reason: null })], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 });
    admin.users.mockResolvedValue({ items: [{ id: 'u1', userName: 'admin', displayName: 'Administrador' }], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 });
    TestBed.configureTestingModule({ imports: [AuditLog], providers: [
      { provide: AuditApi, useValue: api }, { provide: AdminApi, useValue: admin }, { provide: SessionService, useValue: session }] });
  });

  async function create() {
    const fixture = TestBed.createComponent(AuditLog);
    fixture.detectChanges(); await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0)); fixture.detectChanges();
    return fixture;
  }
  const text = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.textContent as string;

  it('muestra los eventos de hoy con usuario, acción en español, autorizador, motivo y resultado', async () => {
    const fixture = await create();
    const filter = api.records.mock.calls[0][0];
    expect(filter.from).toBe(filter.to);
    expect(filter).toMatchObject({ userId: '', action: '', page: 1, pageSize: 50 });
    const content = text(fixture);
    expect(content).toContain('Venta anulada');
    expect(content).toContain('Ventas');
    expect(content).toContain('supervisor');
    expect(content).toContain('Error en el cobro');
    expect(content).toContain('Inicio de sesión fallido');
    expect(content).toContain('Sistema');
    const pills = Array.from(fixture.nativeElement.querySelectorAll('.status-pill') as NodeListOf<HTMLElement>);
    expect(pills.map((pill) => pill.classList.contains('status-pill--warning'))).toEqual([false, true]);
  });

  it('filtra por usuario y acción escritos o elegidos, y vuelve a la primera página (A.19)', async () => {
    const fixture = await create();
    const component = fixture.componentInstance;
    const saleCancelled = component.actionOptions.find(option => option.code === 'SaleCancelled')!;
    component.filter.page = 3; component.userText = 'admin · Administrador'; component.actionText = saleCancelled.text;
    component.find();
    await fixture.whenStable();
    expect(api.records).toHaveBeenLastCalledWith(expect.objectContaining({ userId: 'u1', action: 'SaleCancelled', page: 1 }));
    // Basta con escribir el usuario o un pedazo que coincida con una sola acción, en minúsculas.
    component.userText = 'ADMIN'; component.actionText = saleCancelled.text.toLowerCase().slice(0, -1);
    component.find();
    expect(api.records).toHaveBeenLastCalledWith(expect.objectContaining({ userId: 'u1', action: 'SaleCancelled' }));
    expect(component.actionText).toBe(saleCancelled.text);
    // Vacío es «todos».
    component.userText = ''; component.actionText = ''; component.find();
    expect(api.records).toHaveBeenLastCalledWith(expect.objectContaining({ userId: '', action: '' }));
  });

  it('explica cuando lo escrito no corresponde a un usuario o a una sola acción, sin consultar', async () => {
    const fixture = await create();
    const component = fixture.componentInstance;
    api.records.mockClear();
    component.userText = 'nadie'; component.find();
    expect(component.filterHint()).toContain('No hay un solo usuario');
    component.userText = ''; component.actionText = 'credito'; component.find();
    expect(component.filterHint()).toMatch(/^Hay \d+ acciones con «credito»\. Elige una de la lista/);
    component.actionText = 'zzz'; component.find();
    expect(component.filterHint()).toContain('Ninguna acción tiene «zzz»');
    // Es un aviso junto a los filtros, no un mensaje emergente.
    expect(component.error()).toBe('');
    expect(api.records).not.toHaveBeenCalled();
  });

  it('sugiere acciones mientras se escribe, sin importar tildes, y al elegir una filtra de una vez (punto 15)', async () => {
    const fixture = await create();
    const component = fixture.componentInstance;
    api.records.mockClear();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input[name="actionText"]') as HTMLInputElement;
    input.dispatchEvent(new Event('focus'));
    input.value = 'abono credito'; input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const options = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="option"] span')).map(x => x.textContent?.trim());
    expect(options).toContain('Abono a crédito');
    expect(options).toContain('Abono a crédito anulado');
    const first = (fixture.nativeElement as HTMLElement).querySelector('[role="option"]') as HTMLElement;
    first.dispatchEvent(new MouseEvent('mousedown'));
    await fixture.whenStable();
    expect(api.records).toHaveBeenCalledOnce();
    expect(api.records).toHaveBeenLastCalledWith(expect.objectContaining({ action: expect.stringMatching(/^CreditPayment/), page: 1 }));
    expect(component.filterHint()).toBe('');
  });

  it('dice sobre qué fue cada acción en palabras de la tienda', async () => {
    const fixture = await create();
    const label = (entityType: string) => fixture.componentInstance.entityLabel({ entityType } as never);
    expect(label('User')).toBe('Usuario');
    expect(label('TurnoCaja')).toBe('Turno de caja');
    expect(label('AperturaCajon')).toBe('Cajón de dinero');
    expect(label('AlgoNuevo')).toBe('Algo nuevo');
  });

  it('rechaza un rango de fechas invertido sin consultar', async () => {
    const fixture = await create();
    const component = fixture.componentInstance;
    api.records.mockClear();
    component.filter.from = '2026-11-16'; component.filter.to = '2026-11-01';
    await component.reload();
    expect(api.records).not.toHaveBeenCalled();
    expect(component.error()).toContain('fecha inicial');
  });

  it('despliega el detalle como campo y valor', async () => {
    const fixture = await create();
    const button = fixture.nativeElement.querySelector('.table-action') as HTMLButtonElement;
    button.click(); fixture.detectChanges();
    const detail = text(fixture);
    expect(detail).toContain('Total');
    expect(detail).toContain('5.000');
    expect(fixture.componentInstance.detailRows(record({ details: 'texto libre' }))).toEqual([{ field: 'Detalle', value: 'texto libre' }]);
  });

  it('separa importes, cantidades y decimales del detalle sin cambiar códigos, versiones ni el evento guardado', async () => {
    const fixture = await create();
    const details = JSON.stringify({ total: 1234567.5, totalPaid: 12345, quantity: 1250, version: 1234, documentNumber: 1234567,
      productCode: '00001234', phone: 3101234567, occurredOn: '2026-11-19',
      after: { unitCost: 1234.5678, stock: 10000, productId: 'p-00001234', expectedVersion: 1000 },
      lines: [{ quantity: 2000, amount: -1234.5 }], productIds: [1234, 5678], note: 'Pedido 1234567' });
    const event = record({ details });
    const rows = Object.fromEntries(fixture.componentInstance.detailRows(event).map(row => [row.field, row.value]));
    expect(rows['Total']).toBe('1.234.567,5');
    expect(rows['Total pagado']).toBe('12.345');
    expect(rows['Cantidad']).toBe('1.250');
    // Sin llaves, comillas ni identificadores internos.
    expect(rows['Después']).toBe('Costo unitario: 1.234,5678 · Existencias: 10.000');
    expect(rows['Productos']).toBe('Cantidad: 2.000 · Monto: -1.234,5');
    expect(rows['Product ids']).toBeUndefined();
    expect(rows['Version']).toBeUndefined();
    expect(rows).toMatchObject({ 'Número de documento': '1234567', 'Código del producto': '00001234', 'Teléfono': '3101234567', 'Fecha': '2026-11-19', 'Nota': 'Pedido 1234567' });
    expect(event.details).toBe(details);
    expect(fixture.componentInstance.detailRows(record({ details: '1234.5' }))).toEqual([{ field: 'Detalle', value: '1.234,5' }]);
  });

  it('muestra en español los campos modificados y los códigos en inglés (pruebas del 26/11/2026)', async () => {
    const fixture = await create();
    const details = JSON.stringify({ ModifiedFields: ['status', 'prefix', 'series', 'consecutive', 'supplierDocumentNumber', 'stock', 'averageCost'],
      ChangeSource: 'PURCHASE_RECEIPT', AuthorizationMode: 'ADMINISTRATOR_SELF', PreviousRetailPrice: 0, NewRetailPrice: 1500 });
    const rows = Object.fromEntries(fixture.componentInstance.detailRows(record({ details })).map(row => [row.field, row.value]));
    expect(rows['Campos modificados']).toBe('Estado, Prefijo, Serie, Consecutivo, Número de factura, Existencias, Costo promedio');
    expect(rows['Origen del cambio']).toBe('Recepción de compra');
    expect(rows['Forma de autorización']).toBe('El Administrador, sin segundo autorizador');
    expect(rows['Precio de venta anterior']).toBe('0');
    expect(rows['Precio de venta nuevo']).toBe('1.500');
    const text = JSON.stringify(rows);
    for (const word of ['Modified', 'fields', 'supplierDocumentNumber', 'averageCost', 'PURCHASE']) expect(text).not.toContain(word);
  });

  it('sin USERS.READ no ofrece el filtro de usuario ni consulta la lista de usuarios', async () => {
    permissions = ['AUDIT.READ'];
    const fixture = await create();
    expect(admin.users).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('input[name="userText"]')).toBeNull();
  });

  it('nombra cada acción y su módulo; una acción sin traducir se lee separada en palabras', () => {
    expect(auditActionLabel('CashShiftClosed')).toBe('Turno cerrado');
    expect(auditActionModule('CashShiftClosed')).toBe('Caja');
    expect(auditActionLabel('SomethingNew')).toBe('Something New');
    expect(auditActionModule('SomethingNew')).toBe('Otros');
    const codes = AUDIT_ACTION_GROUPS.flatMap((group) => Object.keys(group.actions));
    expect(new Set(codes).size).toBe(codes.length);
  });
});

describe('AuditApi', () => {
  it('envía cada día local como instantes UTC de inicio y fin', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(AuditApi);
    const http = TestBed.inject(HttpTestingController);
    const pending = api.records({ from: '2026-11-16', to: '2026-11-16', userId: 'u1', action: 'SaleConfirmed', page: 2, pageSize: 50 });
    const request = http.expectOne((r) => r.url === '/api/audit-records');
    expect(request.request.params.get('fromUtc')).toBe(localDay('2026-11-16', false));
    expect(request.request.params.get('toUtc')).toBe(localDay('2026-11-16', true));
    expect(request.request.params.get('actorUserId')).toBe('u1');
    expect(request.request.params.get('action')).toBe('SaleConfirmed');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0, totalPages: 0 });
    await pending;
    http.verify();
    expect(new Date(localDay('2026-11-16', false)).getHours()).toBe(0);
    expect(new Date(localDay('2026-11-16', true)).getHours()).toBe(23);
  });
});
