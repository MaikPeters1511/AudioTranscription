import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Progress bar of a running transcription. Without a value yet it is indeterminate.
 * With `announce`, screen readers hear the progress politely and at most every 10 %.
 */
@Component({
  selector: 'app-job-progress',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="flex items-center gap-2">
      @if (percent() !== undefined) {
        <progress
          class="progress progress-primary w-full"
          [value]="percent()"
          max="100"
          aria-valuemin="0"
          aria-valuemax="100"
          [attr.aria-valuenow]="percent()"
          [attr.aria-label]="'progress.label' | transloco"
        ></progress>
        <span class="text-xs tabular-nums text-base-content/70 shrink-0" aria-hidden="true">{{ percent() }} %</span>
      } @else {
        <progress class="progress progress-primary w-full" [attr.aria-label]="'progress.label' | transloco"></progress>
      }
    </div>
    @if (announce()) {
      <span class="sr-only" aria-live="polite">
        @if (announcedStep(); as step) {
          {{ 'progress.announce' | transloco: { percent: step } }}
        }
      </span>
    }
  `,
})
export class JobProgressComponent {
  readonly percent = input<number | undefined>();
  readonly announce = input(false);

  /** Rounded down to 10 % so the live region changes (and is read) at most every 10 %; nothing below 10 %. */
  readonly announcedStep = computed(() => {
    const percent = this.percent();
    return percent === undefined || percent < 10 ? null : Math.floor(percent / 10) * 10;
  });
}
