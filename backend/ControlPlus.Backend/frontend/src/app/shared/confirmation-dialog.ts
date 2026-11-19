import { Component, DestroyRef, inject, Injectable } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

export interface ConfirmationPrompt {
  title: string;
  message: string;
  confirmText?: string;
  danger?: boolean;
}
export type ConfirmationOptions = ConfirmationPrompt;

@Component({
  selector: 'cp-confirmation-dialog',
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content id="cp-confirmation-description">
      <p class="confirmation-message">{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button id="cp-confirmation-cancel" class="button button--secondary" type="button" [mat-dialog-close]="false">Cancelar</button>
      <button class="button" [class.button--primary]="!data.danger" [class.button--danger]="data.danger" type="button" [mat-dialog-close]="true">{{ data.confirmText || 'Confirmar' }}</button>
    </mat-dialog-actions>`,
  styles: [`
    .confirmation-message { margin:0; padding:14px 16px; border-left:3px solid var(--cp-warning); border-radius:4px; background:var(--cp-warning-soft); color:var(--cp-text); line-height:1.6; white-space:pre-line; overflow-wrap:anywhere; }
    mat-dialog-actions { gap:8px; padding:16px 24px 24px; }
    mat-dialog-actions .button { max-width:100%; }
  `],
})
export class ConfirmationDialogComponent {
  readonly data = inject<ConfirmationPrompt>(MAT_DIALOG_DATA);
}

/** One confirmation at a time, just like the old browser dialog, without blocking JavaScript. */
@Injectable({ providedIn: 'root' })
export class ConfirmationDialog {
  private readonly dialog = inject(MatDialog);
  private active: { ref: MatDialogRef<ConfirmationDialogComponent, boolean>; owner: DestroyRef } | null = null;

  async confirm(prompt: ConfirmationPrompt, owner: DestroyRef): Promise<boolean> {
    if (owner.destroyed || this.active) return false;
    const ref = this.dialog.open<ConfirmationDialogComponent, ConfirmationPrompt, boolean>(ConfirmationDialogComponent, {
      data: prompt, width: '480px', maxWidth: 'calc(100vw - 32px)',
      role: 'alertdialog', ariaModal: true, ariaDescribedBy: 'cp-confirmation-description',
      autoFocus: '#cp-confirmation-cancel', restoreFocus: true,
    });
    this.active = { ref, owner };
    const unregister = owner.onDestroy(() => ref.close(false));
    try {
      return await firstValueFrom(ref.afterClosed(), { defaultValue: false }) === true && !owner.destroyed;
    } finally {
      unregister();
      if (this.active?.ref === ref) this.active = null;
    }
  }

  /** Also supports a parent sheet that closes before Angular destroys its view. */
  cancel(owner: DestroyRef): void {
    if (this.active?.owner === owner) this.active.ref.close(false);
  }
}
