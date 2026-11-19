import { Component, ElementRef, HostListener, inject, Input, signal } from '@angular/core';

let nextId = 0;

/**
 * Ayuda junto a los campos (decisión del 19 de noviembre de 2026): un ícono (i) que explica con una frase para qué
 * sirve un campo difícil. Se ve al pasar el ratón o al pulsarlo (también con el teclado); Esc lo cierra.
 */
@Component({
  selector: 'cp-help',
  template: `
    <button class="help-tip__button" type="button" [attr.aria-label]="'Ayuda: ' + text" [attr.aria-expanded]="open()" [attr.aria-describedby]="open() ? id : null"
      (click)="$event.preventDefault(); $event.stopPropagation(); open.set(!open())" (mouseenter)="open.set(true)" (mouseleave)="open.set(false)" (keydown.escape)="open.set(false)">i</button>
    @if (open()) { <span class="help-tip__bubble" role="tooltip" [id]="id">{{ text }}</span> }`,
  styles: [`
    :host { position:relative; display:inline-flex; vertical-align:middle; margin-left:6px; }
    .help-tip__button { display:grid; place-items:center; width:20px; height:20px; padding:0; border:1.5px solid var(--cp-primary); border-radius:50%; background:var(--cp-surface); color:var(--cp-primary); font:700 12px/1 var(--cp-font); cursor:help; }
    .help-tip__button:hover, .help-tip__button[aria-expanded='true'] { background:var(--cp-primary); color:white; }
    .help-tip__bubble { position:absolute; z-index:30; top:calc(100% + 6px); left:-8px; width:max-content; max-width:280px; padding:9px 12px; border-radius:8px; background:var(--cp-sidebar); color:white; font:400 13px/1.45 var(--cp-font); text-transform:none; letter-spacing:normal; white-space:normal; box-shadow:var(--cp-shadow); }
  `],
})
export class HelpTip {
  @Input({ required: true }) text = '';
  readonly open = signal(false);
  readonly id = `cp-help-${++nextId}`;
  private readonly host = inject(ElementRef<HTMLElement>);

  @HostListener('document:click', ['$event'])
  closeOutside(event: MouseEvent): void {
    if (this.open() && !(this.host.nativeElement as HTMLElement).contains(event.target as Node)) this.open.set(false);
  }
}
