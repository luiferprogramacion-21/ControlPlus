import { HttpHeaders, HttpResponse } from '@angular/common/http';
import { statusKind } from '../core/toast';
import { cashDrawerNotice, withCashDrawerStatus } from './cash-drawer-feedback';

describe('Aviso del cajón de dinero', () => {
  it('lee el resultado del cajón del encabezado sin cambiar la operación confirmada', () => {
    const response = new HttpResponse({ body: { id: 'venta-1', total: 5000 }, headers: new HttpHeaders({ 'X-Cash-Drawer-Status': 'ENVIADO' }) });
    expect(withCashDrawerStatus(response)).toEqual({ id: 'venta-1', total: 5000, drawerStatus: 'ENVIADO' });
  });

  it('ignora un encabezado ausente o desconocido', () => {
    expect(withCashDrawerStatus(new HttpResponse({ body: { id: 'v' } })).drawerStatus).toBeNull();
    const unknown = new HttpResponse({ body: { id: 'v' }, headers: new HttpHeaders({ 'X-Cash-Drawer-Status': 'OMITIDO' }) });
    expect(withCashDrawerStatus(unknown).drawerStatus).toBeNull();
  });

  it('explica cada resultado; solo el envío aceptado es un aviso de éxito', () => {
    expect(cashDrawerNotice('ENVIADO')).toContain('Comprueba');
    expect(statusKind(cashDrawerNotice('ENVIADO'))).toBe('success');
    expect(cashDrawerNotice('FALLIDO')).toContain('lo abra desde Caja');
    expect(statusKind(cashDrawerNotice('FALLIDO'))).toBe('error');
    expect(statusKind(cashDrawerNotice('INCIERTO'))).toBe('error');
    expect(cashDrawerNotice(null)).toBe('');
    expect(cashDrawerNotice(undefined)).toBe('');
  });
});
