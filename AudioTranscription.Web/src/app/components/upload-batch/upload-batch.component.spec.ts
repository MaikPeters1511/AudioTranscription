import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Observable, Subject } from 'rxjs';
import { UploadBatchComponent } from './upload-batch.component';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { TranscriptionSettings } from '../../models/audio-job.model';
import { UploadControl } from '../../services/resumable-upload.service';

type Progress = { progress: number; jobId?: string };

const mp3 = (name: string, size = 10) => new File([new Uint8Array(size)], name, { type: 'audio/mpeg' });

@Component({
  standalone: true,
  imports: [UploadBatchComponent],
  template: `<app-upload-batch [files]="files()" [settings]="settings()" (closed)="closedCount = closedCount + 1" />`,
})
class HostComponent {
  files = signal<File[]>([]);
  settings = signal<TranscriptionSettings>({ model: 'Base' });
  closedCount = 0;
}

describe('UploadBatchComponent (S34)', () => {
  let uploads: { file: File; settings: TranscriptionSettings; subject: Subject<Progress>; unsubscribed: boolean; control?: UploadControl }[];
  const toast = { success: vi.fn(), error: vi.fn(), show: vi.fn() };

  function render(files: File[]) {
    uploads = [];
    toast.success.mockReset();
    toast.show.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [HostComponent, translocoTesting('en')],
      providers: [
        provideRouter([]),
        { provide: ToastService, useValue: toast },
        {
          provide: AudioJobService,
          useValue: {
            formatFileSize: (n: number) => `${n} B`,
            uploadFile: (file: File, settings: TranscriptionSettings, control?: UploadControl) => {
              const subject = new Subject<Progress>();
              const record = { file, settings, subject, unsubscribed: false, control };
              uploads.push(record);
              // Marks the record when the component unsubscribes (cancelled upload)
              return new Observable<Progress>((subscriber) => {
                const sub = subject.subscribe(subscriber);
                return () => {
                  record.unsubscribed = true;
                  sub.unsubscribe();
                };
              });
            },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.files.set(files);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function items(el: HTMLElement) {
    return Array.from(el.querySelectorAll('[data-testid=batch-item]'));
  }

  const status = (item: Element) => item.querySelector('[data-testid=batch-status]')?.textContent?.trim();

  it('starts at most three uploads at a time and the rest wait', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd', 'e'].map((n) => mp3(`${n}.mp3`)));

    expect(uploads.map((u) => u.file.name)).toEqual(['a.mp3', 'b.mp3', 'c.mp3']);
    expect(items(el).map(status)).toEqual(['Uploading', 'Uploading', 'Uploading', 'Waiting', 'Waiting']);
    fixture.destroy();
  });

  it('starts the next file as soon as one finishes', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd'].map((n) => mp3(`${n}.mp3`)));

    uploads[0].subject.next({ progress: 100, jobId: 'job-a' });
    uploads[0].subject.complete();
    fixture.detectChanges();

    expect(uploads.map((u) => u.file.name)).toEqual(['a.mp3', 'b.mp3', 'c.mp3', 'd.mp3']);
    expect(status(items(el)[0])).toBe('Uploaded');
    fixture.destroy();
  });

  it('passes the transcription settings to every upload', () => {
    const { fixture } = render([mp3('a.mp3'), mp3('b.mp3')]);

    expect(uploads.map((u) => u.settings)).toEqual([{ model: 'Base' }, { model: 'Base' }]);
    fixture.destroy();
  });

  it('shows the progress of each file', () => {
    const { fixture, el } = render([mp3('a.mp3'), mp3('b.mp3')]);

    uploads[1].subject.next({ progress: 40 });
    fixture.detectChanges();

    const bars = el.querySelectorAll('progress');
    expect(bars[0].value).toBe(0);
    expect(bars[1].value).toBe(40);
    fixture.destroy();
  });

  it('keeps going when one upload fails', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd'].map((n) => mp3(`${n}.mp3`)));

    uploads[0].subject.error({ error: { detail: 'Unsupported audio' } });
    fixture.detectChanges();

    expect(status(items(el)[0])).toBe('Failed');
    expect(items(el)[0].textContent).toContain('Unsupported audio');
    expect(uploads).toHaveLength(4); // the freed slot went to the fourth file
    expect(status(items(el)[1])).toBe('Uploading');
    fixture.destroy();
  });

  it('marks files that fail the client-side checks without uploading them', () => {
    const { fixture, el } = render([
      new File(['x'], 'notes.txt', { type: 'text/plain' }),
      mp3('ok.mp3'),
    ]);

    expect(uploads.map((u) => u.file.name)).toEqual(['ok.mp3']);
    expect(status(items(el)[0])).toBe('Failed');
    expect(items(el)[0].textContent).toContain('Invalid file format');
    fixture.destroy();
  });

  it('cancels a single running upload and frees its slot', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd'].map((n) => mp3(`${n}.mp3`)));

    (items(el)[1].querySelector('[data-testid=batch-cancel]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(uploads[1].unsubscribed).toBe(true);
    expect(status(items(el)[1])).toBe('Cancelled');
    expect(uploads.map((u) => u.file.name)).toContain('d.mp3');
    fixture.destroy();
  });

  it('cancels a waiting file without ever starting it', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd'].map((n) => mp3(`${n}.mp3`)));

    (items(el)[3].querySelector('[data-testid=batch-cancel]') as HTMLButtonElement).click();
    uploads[0].subject.next({ progress: 100, jobId: 'x' });
    uploads[0].subject.complete();
    fixture.detectChanges();

    expect(uploads.map((u) => u.file.name)).not.toContain('d.mp3');
    expect(status(items(el)[3])).toBe('Cancelled');
    fixture.destroy();
  });

  it('cancels everything that is still open', () => {
    const { fixture, el } = render(['a', 'b', 'c', 'd'].map((n) => mp3(`${n}.mp3`)));
    uploads[0].subject.next({ progress: 100, jobId: 'x' });
    uploads[0].subject.complete();
    fixture.detectChanges();

    (el.querySelector('[data-testid=batch-cancel-all]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(items(el).map(status)).toEqual(['Uploaded', 'Cancelled', 'Cancelled', 'Cancelled']);
    fixture.destroy();
  });

  it('announces the summary in a polite live region', () => {
    const { fixture, el } = render([mp3('a.mp3'), mp3('b.mp3')]);
    uploads[0].subject.next({ progress: 100, jobId: 'x' });
    uploads[0].subject.complete();
    uploads[1].subject.error({ error: {} });
    fixture.detectChanges();

    const live = el.querySelector('[aria-live=polite]');
    expect(live?.textContent).toContain('1 of 2 uploaded');
    expect(live?.textContent).toContain('1 failed');
    fixture.destroy();
  });

  it('offers the queue and a way back once everything is settled', () => {
    const { fixture, el } = render([mp3('a.mp3')]);
    expect(el.querySelector('[data-testid=batch-done]')).toBeNull();

    uploads[0].subject.next({ progress: 100, jobId: 'x' });
    uploads[0].subject.complete();
    fixture.detectChanges();

    expect(el.querySelector('a[href="/queue"]')).not.toBeNull();
    (el.querySelector('[data-testid=batch-more]') as HTMLButtonElement).click();
    expect(fixture.componentInstance.closedCount).toBe(1);
    fixture.destroy();
  });

  it('confirms with a toast when all files are uploaded', () => {
    const { fixture } = render([mp3('a.mp3'), mp3('b.mp3')]);

    for (const u of uploads) {
      u.subject.next({ progress: 100, jobId: 'x' });
      u.subject.complete();
    }
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('2 files uploaded. Processing started.');
    fixture.destroy();
  });

  it('stops the running uploads when the component is destroyed', () => {
    const { fixture } = render([mp3('a.mp3'), mp3('b.mp3')]);

    fixture.destroy();

    expect(uploads.every((u) => u.unsubscribed)).toBe(true);
  });

  describe('pausing (S27)', () => {
    const pauseButton = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('[data-testid=batch-pause]');

    it('offers no pause for an upload that cannot be resumed', () => {
      const { el } = render([mp3('klein.mp3')]);

      expect(pauseButton(el)).toBeNull();
    });

    it('pauses and resumes an upload that can be resumed', () => {
      const { fixture, el } = render([mp3('gross.mp3')]);
      const target = { pause: vi.fn(), resume: vi.fn() };
      uploads[0].control!.attach(target);
      fixture.detectChanges();

      pauseButton(el)!.click();
      fixture.detectChanges();
      expect(target.pause).toHaveBeenCalled();
      expect(pauseButton(el)!.textContent).toContain('Resume');
      expect(pauseButton(el)!.getAttribute('aria-pressed')).toBe('true');

      pauseButton(el)!.click();
      fixture.detectChanges();
      expect(target.resume).toHaveBeenCalled();
      expect(pauseButton(el)!.textContent).toContain('Pause');
    });

    it('does not offer the pause once the file is done', () => {
      const { fixture, el } = render([mp3('gross.mp3')]);
      uploads[0].control!.attach({ pause: vi.fn(), resume: vi.fn() });
      uploads[0].subject.next({ progress: 100, jobId: 'job-1' });
      fixture.detectChanges();

      expect(pauseButton(el)).toBeNull();
    });
  });
});
