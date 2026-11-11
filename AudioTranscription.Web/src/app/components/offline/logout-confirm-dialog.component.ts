import { Component, ElementRef, afterNextRender, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Asks before signing out with recordings that were not uploaded yet (S33-T4): they would be deleted.
 * A modal dialog; the focus starts on the safe choice and Escape cancels.
 */
@Component({
  selector: 'app-logout-confirm-dialog',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="modal modal-open">
      <div
        class="modal-box"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="logout-confirm-title"
        aria-describedby="logout-confirm-message"
        (keydown)="onKeydown($event)"
      >
        <h2 id="logout-confirm-title" class="text-lg font-bold">{{ 'offline.logout.title' | transloco }}</h2>
        <p id="logout-confirm-message" class="py-4">{{ 'offline.logout.message' | transloco: { count: count() } }}</p>
        <div class="modal-action">
          <button #cancelButton type="button" class="btn btn-primary min-h-11" data-action="cancel" (click)="cancelled.emit()">
            {{ 'offline.logout.cancel' | transloco }}
          </button>
          <button #confirmButton type="button" class="btn btn-error btn-outline min-h-11" data-action="confirm" (click)="confirmed.emit()">
            {{ 'offline.logout.confirm' | transloco }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class LogoutConfirmDialogComponent {
  readonly count = input.required<number>();
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');
  private confirmButton = viewChild.required<ElementRef<HTMLButtonElement>>('confirmButton');

  constructor() {
    afterNextRender(() => this.cancelButton().nativeElement.focus());
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.cancelled.emit();
      return;
    }
    if (event.key === 'Tab') {
      // Keep the focus inside the dialog: two buttons, so Tab and Shift+Tab simply swap them
      const cancel = this.cancelButton().nativeElement;
      const confirm = this.confirmButton().nativeElement;
      const first = cancel;
      const last = confirm;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
  }
}
