import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WindowSheet } from './window-sheet';

@Component({
  imports: [WindowSheet],
  template: `
    <button id="opener" type="button" (click)="open.set(true)">Abrir</button>
    @if (open()) {
      <div class="module-form-sheet" role="dialog" (keydown.escape)="ownEscape && close()">
        <div class="module-form-sheet__backdrop" (click)="close()"></div>
        <form class="panel module-form"><div class="panel-heading"><h2>Nuevo cliente</h2></div>
          <input id="name" autofocus>
          <div class="form-actions"><button class="button" type="submit">Guardar</button><button type="button" (click)="close()">Cancelar</button></div>
        </form>
      </div>
    }`,
})
class Host {
  readonly open = signal(true);
  ownEscape = false;
  closes = 0;
  close(): void { this.closes++; this.open.set(false); }
}

describe('Ventanas como en Windows', () => {
  async function render(ownEscape = false) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.ownEscape = ownEscape;
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }
  const sheet = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.querySelector('.module-form-sheet') as HTMLElement | null;
  const wait = () => new Promise(resolve => setTimeout(resolve));

  it('pone una X en el título que cierra como «Cancelar»', async () => {
    const fixture = await render();
    const close = fixture.nativeElement.querySelector('.panel-heading .window-close') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Cerrar');
    expect(close.title).toBe('Cerrar (Esc)');
    close.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.closes).toBe(1);
    expect(sheet(fixture)).toBeNull();
  });

  it('Esc cierra la ventana aunque la pantalla no lo maneje', async () => {
    const fixture = await render();
    sheet(fixture)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await wait();
    fixture.detectChanges();
    expect(fixture.componentInstance.closes).toBe(1);
    expect(sheet(fixture)).toBeNull();
  });

  it('no cierra dos veces cuando la pantalla ya maneja Esc', async () => {
    const fixture = await render(true);
    sheet(fixture)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await wait();
    fixture.detectChanges();
    expect(fixture.componentInstance.closes).toBe(1);
  });

  it('lleva el foco al primer campo', async () => {
    const fixture = await render();
    expect(document.activeElement?.id).toBe('name');
    fixture.destroy();
  });
});
