import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { PrintSettings } from '../admin/print-settings';
import { EstablishmentPanel } from './establishment-panel';
import { ShiftModePanel } from './shift-mode-panel';
import { SettingsPanel } from './settings-panel';

type Section = 'establishment' | 'shift-mode' | 'settings' | 'printing';
interface Tile { id: Section; title: string; description: string; icon: string; }

/**
 * Configuración del negocio. Decisión del 19 de noviembre de 2026: en lugar de pestañas, muestra un cuadro por
 * sección; al pulsarlo se abre su contenido con un botón para volver. Si solo hay una sección disponible, se abre directo.
 */
@Component({
  selector: 'cp-config',
  imports: [MatButtonModule, Icon, EstablishmentPanel, ShiftModePanel, SettingsPanel, PrintSettings],
  templateUrl: './config.html',
  styleUrls: ['../directory.scss', '../admin/admin.scss', './config.scss'],
})
export class Config {
  readonly session = inject(SessionService);
  get canConfigure(): boolean { return this.session.has('CONFIGURATION.MANAGE'); }
  get canShiftMode(): boolean { return this.session.has('CASH.SHIFT_MODE_MANAGE'); }

  readonly tiles: Tile[] = [
    ...(this.canConfigure ? [{ id: 'establishment' as const, title: 'Establecimiento', description: 'Nombre, NIT, dirección, teléfono y textos del ticket.', icon: 'dashboard' }] : []),
    ...(this.canShiftMode ? [{ id: 'shift-mode' as const, title: 'Caja y turnos', description: 'Turno individual o compartido por defecto.', icon: 'cash' }] : []),
    ...(this.canConfigure ? [
      { id: 'settings' as const, title: 'Cambios, apartados y créditos', description: 'Plazos, pago inicial, vencimientos, morosos y mayorista.', icon: 'settings' },
      { id: 'printing' as const, title: 'Impresión', description: 'Impresora de tickets y etiquetas, y cola de impresión.', icon: 'sales' },
    ] : []),
  ];
  /** Sección abierta; nula muestra los cuadros. */
  readonly tab = signal<Section | null>(this.tiles.length === 1 ? this.tiles[0].id : null);
  get current(): Tile | undefined { return this.tiles.find(tile => tile.id === this.tab()); }
  open(section: Section): void { this.tab.set(section); }
  back(): void { this.tab.set(null); }
}
