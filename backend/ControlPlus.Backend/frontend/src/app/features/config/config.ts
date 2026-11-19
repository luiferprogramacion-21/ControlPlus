import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { PrintSettings } from '../admin/print-settings';
import { EstablishmentPanel } from './establishment-panel';
import { ShiftModePanel } from './shift-mode-panel';
import { NumberingPanel } from './numbering-panel';
import { ReasonsPanel } from './reasons-panel';
import { SettingsPanel } from './settings-panel';

type Tab = 'numbering' | 'establishment' | 'shift-mode' | 'settings' | 'printing' | 'reasons';

/** Business configuration for the Administrator: numbering, change and layaway parameters, printer and reasons. */
@Component({
  selector: 'cp-config',
  imports: [MatButtonModule, Icon, EstablishmentPanel, ShiftModePanel, NumberingPanel, SettingsPanel, PrintSettings, ReasonsPanel],
  templateUrl: './config.html',
  styleUrls: ['../directory.scss', '../admin/admin.scss'],
})
export class Config {
  readonly session = inject(SessionService);
  readonly tab = signal<Tab>(this.session.has('NUMBERING.MANAGE') ? 'numbering' : this.session.has('CONFIGURATION.MANAGE') ? 'establishment' : 'shift-mode');
  get canNumber(): boolean { return this.session.has('NUMBERING.MANAGE'); }
  get canConfigure(): boolean { return this.session.has('CONFIGURATION.MANAGE'); }
  get canShiftMode(): boolean { return this.session.has('CASH.SHIFT_MODE_MANAGE'); }
  switchTab(tab: Tab): void { this.tab.set(tab); }
}
