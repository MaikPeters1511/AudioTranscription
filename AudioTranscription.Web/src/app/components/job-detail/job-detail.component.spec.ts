import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { JobDetailComponent } from './job-detail.component';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJob, AudioJobStatus } from '../../models/audio-job.model';
import { translocoTesting } from '../../i18n/transloco-testing';

const completedJob = (overrides: Partial<AudioJob>): AudioJob =>
  ({
    id: 'job-1',
    fileName: 'meeting.mp3',
    fileSizeBytes: 1024,
    contentType: 'audio/mpeg',
    status: AudioJobStatus.Completed,
    createdAtUtc: '2026-09-24T10:00:00Z',
    ...overrides,
  }) as AudioJob;

describe('JobDetailComponent', () => {
  let jobService: AudioJobService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [JobDetailComponent, translocoTesting('de')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    jobService = TestBed.inject(AudioJobService);
  });

  function render(job: AudioJob) {
    const fixture = TestBed.createComponent(JobDetailComponent);
    jobService.selectedJob.set(job);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the post-processed transcript when available', () => {
    const fixture = render(
      completedJob({ rawTranscript: 'aehm hallo welt', processedTranscript: 'Hallo Welt.' }),
    );

    expect(fixture.componentInstance.transcript()).toBe('Hallo Welt.');
    expect(fixture.nativeElement.textContent).toContain('Hallo Welt.');
    expect(fixture.nativeElement.textContent).not.toContain('aehm hallo welt');
  });

  it('falls back to the raw transcript without post-processing', () => {
    const fixture = render(completedJob({ rawTranscript: 'hallo welt' }));

    expect(fixture.componentInstance.transcript()).toBe('hallo welt');
    expect(fixture.componentInstance.wordCount()).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('hallo welt');
  });

  it('does not render a transcript section for unfinished jobs', () => {
    const fixture = render(
      completedJob({ status: AudioJobStatus.Processing, rawTranscript: 'teilweise' }),
    );

    expect(fixture.nativeElement.textContent).not.toContain('teilweise');
  });

  it('renders labels in the active language', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    const fixture = render(completedJob({ rawTranscript: 'hello world' }));

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Back to list');
    expect(text).toContain('2 words · 11 characters');
    expect(text).toContain('Completed');
    expect(text).not.toContain('Zurück zur Liste');
  });

  it('shows the model and whether the language was set at upload', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    const fixture = render(completedJob({ model: 'Small', requestedLanguage: 'de', language: 'de' }));

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Small');
    expect(text).toContain('German');
    expect(text).toContain('Set at upload');
  });

  it('marks an automatically detected language', () => {
    const fixture = render(completedJob({ model: 'Base', language: 'en' }));

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Englisch');
    expect(text).toContain('Automatisch erkannt');
  });

  it('offers subtitle downloads for completed jobs', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    const fixture = render(completedJob({ rawTranscript: 'hallo' }));
    TestBed.inject(HttpTestingController).expectOne('/api/audio-jobs/job-1/segments').flush([]);

    const srt = fixture.nativeElement.querySelector('a[data-subtitles="srt"]') as HTMLAnchorElement;
    const vtt = fixture.nativeElement.querySelector('a[data-subtitles="vtt"]') as HTMLAnchorElement;
    expect(srt.getAttribute('href')).toBe('/api/audio-jobs/job-1/subtitles?format=srt');
    expect(srt.hasAttribute('download')).toBe(true);
    expect(srt.getAttribute('aria-label')).toBe('Download subtitles as SRT');
    expect(vtt.getAttribute('href')).toBe('/api/audio-jobs/job-1/subtitles?format=vtt');
  });

  it('offers no subtitles or player for unfinished jobs', () => {
    const fixture = render(completedJob({ status: AudioJobStatus.Failed }));

    expect(fixture.nativeElement.querySelector('a[data-subtitles]')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-transcript-player')).toBeNull();
  });

  it('offers the job actions and explains a cancelled job', () => {
    const fixture = render(completedJob({ status: AudioJobStatus.Cancelled }));

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Die Transkription wurde abgebrochen.');
    expect(fixture.nativeElement.querySelector('app-job-actions [data-action="retry"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-job-actions [data-action="delete"]')).not.toBeNull();
  });

  it('returns to the list after deleting the job via its actions', async () => {
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
    const fixture = render(completedJob({ rawTranscript: 'x' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const el: HTMLElement = fixture.nativeElement;

    el.querySelector<HTMLButtonElement>('[data-action="delete"]')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-confirm="delete"]')!.click();
    TestBed.inject(HttpTestingController).expectOne('/api/audio-jobs/job-1').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/jobs']);
  });

  it('returns to the list when another client deletes the open job', () => {
    render(completedJob({ rawTranscript: 'x' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    jobService.removeJobFromList('job-1'); // what the SignalR JobDeleted handler does

    expect(navigate).toHaveBeenCalledWith(['/jobs']);
  });
});
