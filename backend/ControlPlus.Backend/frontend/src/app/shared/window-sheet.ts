import { AfterViewInit, Directive, ElementRef, inject, NgZone, OnDestroy } from '@angular/core';
import { FocusTrap, FocusTrapFactory } from '@angular/cdk/a11y';

const CLOSE_ICON = '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true" focusable="false"><path d="M6 6l12 12 M6 18L18 6"/></svg>';

/**
 * Ventanas como en Windows (decisión del 19 de noviembre de 2026). Se aplica sola a todo `.module-form-sheet`:
 * - pone una X arriba a la derecha del título («Cerrar (Esc)»);
 * - Esc cierra la ventana aunque la pantalla no lo maneje;
 * - mantiene el foco dentro de la ventana y lo devuelve al botón que la abrió al cerrarse.
 * Cerrar equivale a tocar el fondo oscuro: cada pantalla ya tiene allí su acción de cancelar, así la X y Esc
 * hacen exactamente lo mismo que «Cancelar» y no se duplica lógica.
 */
@Directive({ selector: '.module-form-sheet' })
export class WindowSheet implements AfterViewInit, OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly focusTraps = inject(FocusTrapFactory);
  private readonly zone = inject(NgZone);
  private readonly opener = typeof document !== 'undefined' ? document.activeElement as HTMLElement | null : null;
  private trap: FocusTrap | null = null;
  private closeButton: HTMLButtonElement | null = null;
  private readonly onKeydown = (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return;
    // Si la pantalla ya cerró la ventana con su propio Esc, no se hace nada más.
    setTimeout(() => { if (this.host.isConnected) this.zone.run(() => this.close()); });
  };

  ngAfterViewInit(): void {
    this.host.addEventListener('keydown', this.onKeydown);
    this.addCloseButton();
    // Algunas ventanas ya atrapan el foco con cdkTrapFocus; las demás lo atrapan aquí.
    if (!this.host.hasAttribute('cdktrapfocus')) {
      this.trap = this.focusTraps.create(this.host);
      const focusable = this.host.querySelector<HTMLElement>('[autofocus]');
      if (!this.host.contains(document.activeElement)) {
        if (focusable) focusable.focus(); else void this.trap.focusFirstTabbableElementWhenReady();
      }
    }
  }

  ngOnDestroy(): void {
    this.host.removeEventListener('keydown', this.onKeydown);
    this.trap?.destroy();
    const opener = this.opener;
    if (opener?.isConnected && opener !== document.body) setTimeout(() => { if (!document.querySelector('.module-form-sheet')) opener.focus(); });
  }

  close(): void {
    const backdrop = Array.from(this.host.children).find(child => child.classList.contains('module-form-sheet__backdrop')) as HTMLElement | undefined;
    backdrop?.click();
  }

  private addCloseButton(): void {
    if (this.host.querySelector('.dialog-close, .window-close')) return;
    const heading = this.host.querySelector<HTMLElement>('.panel-heading, .dialog-heading') ?? this.host.querySelector<HTMLElement>('h2')?.parentElement ?? null;
    if (!heading) return;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'window-close';
    button.setAttribute('aria-label', 'Cerrar');
    button.title = 'Cerrar (Esc)';
    button.innerHTML = CLOSE_ICON;
    button.addEventListener('click', () => this.zone.run(() => this.close()));
    heading.classList.add('window-heading');
    heading.appendChild(button);
    this.closeButton = button;
  }
}
