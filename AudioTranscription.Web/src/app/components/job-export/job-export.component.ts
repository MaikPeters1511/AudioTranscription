import { Component, computed, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

export type ExportFormat = 'docx' | 'md' | 'json' | 'pdf';

let nextId = 0;

/** Export menu (S21): format and options, then a plain download link (the browser sends the session cookie). */
@Component({
  selector: 'app-job-export',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <details class="dropdown dropdown-end">
      <summary class="btn btn-sm btn-outline" data-testid="export-summary">{{ 'jobExport.title' | transloco }}</summary>
      <div class="dropdown-content z-10 bg-base-200 rounded-box shadow p-3 w-64 space-y-3">
        <div class="flex flex-col gap-1">
          <label class="text-sm" [for]="formatId">{{ 'jobExport.format' | transloco }}</label>
          <select
            class="select select-sm"
            data-testid="export-format"
            [id]="formatId"
            (change)="format.set($any($event.target).value)"
          >
            @for (f of formats; track f) {
              <option [value]="f" [selected]="f === format()">{{ 'jobExport.formats.' + f | transloco }}</option>
            }
          </select>
        </div>

        @if (!variantId()) {
          <label class="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              class="checkbox checkbox-sm"
              data-testid="export-timestamps"
              [checked]="timestamps()"
              (change)="timestamps.set($any($event.target).checked)"
            />
            <span>{{ 'jobExport.timestamps' | transloco }}</span>
          </label>
          <label class="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              class="checkbox checkbox-sm"
              data-testid="export-speakers"
              [checked]="speakers()"
              (change)="speakers.set($any($event.target).checked)"
            />
            <span>{{ 'jobExport.speakers' | transloco }}</span>
          </label>
        }

        <a
          class="btn btn-sm btn-primary w-full"
          data-testid="export-download"
          download
          [href]="href()"
          [attr.aria-label]="'jobExport.downloadLabel' | transloco"
        >{{ 'jobExport.download' | transloco }}</a>
      </div>
    </details>
  `,
})
export class JobExportComponent {
  readonly jobId = input.required<string>();
  /** Id of the displayed post-processed version; unset for the original transcript. */
  readonly variantId = input<string | undefined>(undefined);

  readonly formats: readonly ExportFormat[] = ['docx', 'md', 'json', 'pdf'];
  readonly formatId = `export-format-${nextId++}`;
  readonly format = signal<ExportFormat>('docx');
  readonly timestamps = signal(true);
  readonly speakers = signal(true);

  readonly href = computed(() => {
    const base = `/api/audio-jobs/${encodeURIComponent(this.jobId())}/export?format=${this.format()}`;
    const variant = this.variantId();
    return variant
      ? `${base}&variant=${encodeURIComponent(variant)}`
      : `${base}&timestamps=${this.timestamps()}&speakers=${this.speakers()}`;
  });
}
