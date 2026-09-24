import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { JobDetailComponent } from './job-detail.component';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { AudioJob, AudioJobStatus, PostProcessingMode, TranscriptVariant, VariantStatus } from '../../models/audio-job.model';
import { translocoTesting } from '../../i18n/transloco-testing';

const job: AudioJob = {
  id: 'job-1',
  fileName: 'meeting.mp3',
  fileSizeBytes: 1024,
  contentType: 'audio/mpeg',
  status: AudioJobStatus.Completed,
  createdAtUtc: '2026-09-24T10:00:00Z',
  rawTranscript: 'Hallo Welt',
} as AudioJob;

const options = { models: ['Base'], defaultModel: 'Base', languages: ['de', 'en'], postProcessingEnabled: true };

describe('JobDetailComponent post-processing actions (S10-T6)', () => {
  let jobService: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [JobDetailComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    jobService = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  function render(postProcessingEnabled = true, variants: TranscriptVariant[] = []): ComponentFixture<JobDetailComponent> {
    const fixture = TestBed.createComponent(JobDetailComponent);
    jobService.selectedJob.set(job);
    jobService.variants.set(variants);
    fixture.detectChanges();
    http.expectOne('/api/transcription-options').flush({ ...options, postProcessingEnabled });
    fixture.detectChanges();
    return fixture;
  }

  function buttons(fixture: ComponentFixture<JobDetailComponent>): HTMLButtonElement[] {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).filter((b) =>
      /Edited|Summary|Bullet points|Action items|Translate/.test(b.textContent ?? ''),
    );
  }

  it('offers a button per mode plus a language select for translate', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;

    const labels = buttons(fixture).map((b) => b.textContent!.trim());
    expect(labels).toEqual(['Edited', 'Summary', 'Bullet points', 'Action items', 'Translate']);
    expect(el.querySelector('#translate-language')).not.toBeNull();
  });

  it('hides the actions entirely when post-processing is unavailable', () => {
    const fixture = render(false);

    expect(buttons(fixture).length).toBe(0);
  });

  it('requests a variant and shows it as pending until it resolves', () => {
    const fixture = render();
    const summaryButton = buttons(fixture).find((b) => b.textContent?.trim() === 'Summary')!;

    summaryButton.click();
    fixture.detectChanges();
    const post = http.expectOne({ url: '/api/audio-jobs/job-1/variants', method: 'POST' });
    expect(post.request.body).toEqual({ mode: PostProcessingMode.Summary, targetLanguage: undefined });
    post.flush({ id: 'v1', mode: PostProcessingMode.Summary, status: VariantStatus.Pending, createdAtUtc: '2026-09-24T10:02:00Z' });
    fixture.detectChanges();

    expect(summaryButton.disabled).toBe(true);
    expect(summaryButton.getAttribute('aria-busy')).toBe('true');
  });

  it('sends the selected target language when translating', () => {
    const fixture = render();
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#translate-language');
    select.value = 'en';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    buttons(fixture).find((b) => b.textContent?.trim() === 'Translate')!.click();

    const post = http.expectOne({ url: '/api/audio-jobs/job-1/variants', method: 'POST' });
    expect(post.request.body).toEqual({ mode: PostProcessingMode.Translate, targetLanguage: 'en' });
    post.flush({ id: 'v2', mode: PostProcessingMode.Translate, targetLanguage: 'en', status: VariantStatus.Pending, createdAtUtc: '2026-09-24T10:02:00Z' });
  });

  it('does not stay disabled once a variant of that mode has completed', () => {
    const fixture = render(true, [
      { id: 'v8', mode: PostProcessingMode.Cleanup, status: VariantStatus.Completed, text: 'Fertig', createdAtUtc: '2026-09-24T10:00:00Z' },
    ]);

    const editedButton = buttons(fixture).find((b) => b.textContent?.trim() === 'Edited')!;
    expect(editedButton.disabled).toBe(false);
    expect(editedButton.getAttribute('aria-busy')).toBeNull();
  });

  it('does not distinguish target languages when checking pending state (regression guard)', () => {
    const fixture = render(true, [
      { id: 'v4', mode: PostProcessingMode.Translate, targetLanguage: 'de', status: VariantStatus.Pending, createdAtUtc: '2026-09-24T10:00:00Z' },
    ]);

    expect(fixture.componentInstance.isPending(PostProcessingMode.Translate, 'de')).toBe(true);
    expect(fixture.componentInstance.isPending(PostProcessingMode.Translate, 'en')).toBe(false);
  });

  it('only shows completed variants as tabs, not pending or failed ones', () => {
    const fixture = render(true, [
      { id: 'v5', mode: PostProcessingMode.Cleanup, status: VariantStatus.Pending, createdAtUtc: '2026-09-24T10:00:00Z' },
      { id: 'v6', mode: PostProcessingMode.Summary, status: VariantStatus.Failed, createdAtUtc: '2026-09-24T10:00:00Z' },
    ]);

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeNull();
  });

  it('labels a completed translation tab with its target language, not as "Edited"', () => {
    const fixture = render(true, [
      { id: 'v7', mode: PostProcessingMode.Translate, targetLanguage: 'fr', status: VariantStatus.Completed, text: 'Bonjour', createdAtUtc: '2026-09-24T10:00:00Z' },
    ]);

    const labels = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('[role="tab"]')).map((t) => t.textContent!.trim());
    expect(labels).toContain('Translation (French)');
    expect(labels).not.toContain('Edited');
  });

  it('lists failed variants with their error message', () => {
    const fixture = render(true, [
      { id: 'v3', mode: PostProcessingMode.ActionItems, status: VariantStatus.Failed, errorMessage: 'Ollama nicht erreichbar', createdAtUtc: '2026-09-24T10:00:00Z' },
    ]);

    expect(fixture.nativeElement.textContent).toContain('Ollama nicht erreichbar');
  });

  it('shows an error toast when the request itself fails', () => {
    const fixture = render();
    const toastError = vi.spyOn(TestBed.inject(ToastService), 'error');
    buttons(fixture).find((b) => b.textContent?.trim() === 'Edited')!.click();

    http.expectOne({ url: '/api/audio-jobs/job-1/variants', method: 'POST' })
      .flush({ title: 'Conflict', detail: 'Job ist nicht abgeschlossen.' }, { status: 409, statusText: 'Conflict' });

    expect(toastError).toHaveBeenCalledWith('Job ist nicht abgeschlossen.');
  });
});
