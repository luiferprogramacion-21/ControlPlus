import { OperatorPresence } from './operator-presence';

describe('OperatorPresence (opción B del turno compartido, 26/11/2026)', () => {
  it('guarda quién pasó su carné y sus permisos; sin carné activo no hay nadie', () => {
    const presence = new OperatorPresence();
    presence.update({ requiresOperatorSession: true, ownOperatorSession: { id: 's1' }, activeOperatorName: 'María', activeOperatorRole: 'CAJERO', activeOperatorPermissions: ['SALES.CREATE'] });
    expect(presence.active()?.name).toBe('María');
    expect(presence.roleLabel()).toBe('Cajero');
    expect(presence.has('SALES.CREATE')).toBe(true);
    expect(presence.has('REPORTS.FINANCIAL_READ')).toBe(false);
    presence.update({ requiresOperatorSession: true, ownOperatorSession: null, activeOperatorPermissions: ['SALES.CREATE'] });
    expect(presence.active()).toBeNull();
    presence.update({ requiresOperatorSession: false, ownOperatorSession: { id: 's1' }, activeOperatorPermissions: ['SALES.CREATE'] });
    expect(presence.active()).toBeNull();
    expect(presence.has('SALES.CREATE')).toBe(false);
  });
});
