import { computed, signal } from '@angular/core';

export type SegmentEditError = 'conflict' | 'failed';

/** Edit state of the one segment being corrected in the player (S32); at most one segment is edited at a time. */
export class SegmentEditState {
  /** Index of the segment being edited; null when idle. */
  readonly index = signal<number | null>(null);
  readonly draft = signal('');
  readonly saving = signal(false);
  readonly error = signal<SegmentEditError | null>(null);

  private original = signal('');

  /** A draft can be saved when it is not empty, differs from the text it started from and nothing is being saved. */
  readonly canSave = computed(() => {
    const draft = this.draft().trim();
    return this.index() !== null && !this.saving() && draft.length > 0 && draft !== this.original().trim();
  });

  start(index: number, text: string): void {
    this.index.set(index);
    this.original.set(text);
    this.draft.set(text);
    this.saving.set(false);
    this.error.set(null);
  }

  beginSave(): void {
    this.saving.set(true);
    this.error.set(null);
  }

  /** The draft is kept so the user does not lose the correction. */
  fail(error: SegmentEditError): void {
    this.saving.set(false);
    this.error.set(error);
  }

  finish(): void {
    this.cancel();
  }

  cancel(): void {
    this.index.set(null);
    this.original.set('');
    this.draft.set('');
    this.saving.set(false);
    this.error.set(null);
  }
}
