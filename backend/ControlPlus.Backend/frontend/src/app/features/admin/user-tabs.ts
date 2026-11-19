import { Component, input, output } from '@angular/core';

export type UserTab = 'datos' | 'rol' | 'permisos' | 'contrasena' | 'carne';
export interface UserTabOption { id: UserTab; label: string; }

/**
 * Pestañas de la ficha de usuario (decisión del 19 de noviembre de 2026): Datos, Rol, Permisos, Contraseña y Carné
 * en una sola ventana, en lugar de un botón aparte para cada cosa en la fila.
 */
@Component({
  selector: 'cp-user-tabs',
  template: `
    <div class="user-tabs" role="tablist" aria-label="Secciones del usuario">
      @for (tab of tabs(); track tab.id) {
        <button class="user-tabs__tab" type="button" role="tab" [attr.aria-selected]="tab.id === active()" [class.is-active]="tab.id === active()" (click)="tab.id !== active() && selected.emit(tab.id)">{{ tab.label }}</button>
      }
    </div>`,
  styles: [`
    .user-tabs { display:flex; flex-wrap:wrap; gap:4px; margin:0 0 16px; border-bottom:1px solid var(--cp-border); }
    .user-tabs__tab { min-height:40px; padding:0 14px; border:0; border-bottom:3px solid transparent; background:transparent; color:var(--cp-text-secondary); font:600 15px var(--cp-font); cursor:pointer; }
    .user-tabs__tab:hover { color:var(--cp-primary); }
    .user-tabs__tab.is-active { border-bottom-color:var(--cp-primary); color:var(--cp-primary); }
  `],
})
export class UserTabs {
  readonly tabs = input.required<UserTabOption[]>();
  readonly active = input.required<UserTab>();
  readonly selected = output<UserTab>();
}
