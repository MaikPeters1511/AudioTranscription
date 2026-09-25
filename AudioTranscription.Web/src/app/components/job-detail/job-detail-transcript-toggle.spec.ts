import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { JobDetailComponent } from './job-detail.component';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJob, AudioJobStatus, PostProcessingMode, TranscriptVariant, VariantStatus } from '../../models/audio-job.model';
import { translocoTesting } from '../../i18n/transloco-testing';

const job = (overrides: Partial<AudioJob> = {}): AudioJob =>
  ({
    id: 'job-1',
    fileName: 'meeting.mp3',
    fileSizeBytes: 1024,
    contentType: 'audio/mpeg',
    status: AudioJobStatus.Completed,
    createdAtUtc: '2026-09-24T10:00:00Z',
    rawTranscript: 'aehm also hallo welt',
    ...overrides,
  }) as AudioJob;

const cleanupVariant = (text = 'Hallo Welt.', overrides: Partial<TranscriptVariant> = {}): TranscriptVariant => ({
  id: 'variant-1',
  mode: PostProcessingMode.Cleanup,
  status: VariantStatus.Completed,
  text,
  createdAtUtc: '2026-09-24T10:01:00Z',
  ...overrides,
});

describe('JobDetailComponent transcript version tabs (S04-T4, generalized to variants in S10)', () => {
  let jobService: AudioJobService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [JobDetailComponent, translocoTesting('de')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    jobService = TestBed.inject(AudioJobService);
  });

  function render(value: AudioJob, variants: TranscriptVariant[] = []): ComponentFixture<JobDetailComponent> {
    const fixture = TestBed.createComponent(JobDetailComponent);
    jobService.selectedJob.set(value);
    jobService.variants.set(variants);
    fixture.detectChanges();
    return fixture;
  }

  const tabs = (fixture: ComponentFixture<JobDetailComponent>): HTMLButtonElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]'));
  const tab = (fixture: ComponentFixture<JobDetailComponent>, name: string) =>
    tabs(fixture).find((t) => t.textContent?.trim() === name)!;
  const panelText = (fixture: ComponentFixture<JobDetailComponent>) =>
    (fixture.nativeElement.querySelector('#transcript-panel') as HTMLElement).textContent?.trim();
  const press = (fixture: ComponentFixture<JobDetailComponent>, target: HTMLElement, key: string) => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    fixture.detectChanges();
  };

  it('is not shown when no variant has been generated yet', () => {
    const fixture = render(job());

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('aehm also hallo welt');
  });

  it('offers both versions and selects the original one by default', () => {
    const fixture = render(job(), [cleanupVariant()]);

    const tablist: HTMLElement = fixture.nativeElement.querySelector('[role="tablist"]');
    expect(tablist.getAttribute('aria-label')).toBe('Transkript-Fassung');
    expect(tabs(fixture).map((t) => [t.textContent?.trim(), t.getAttribute('aria-selected')])).toEqual([
      ['Original', 'true'],
      ['Bearbeitet', 'false'],
    ]);
    expect(panelText(fixture)).toBe('aehm also hallo welt');
  });

  it('shows the generated version when its tab is selected', () => {
    const fixture = render(job(), [cleanupVariant()]);

    tab(fixture, 'Bearbeitet').click();
    fixture.detectChanges();

    expect(tab(fixture, 'Bearbeitet').getAttribute('aria-selected')).toBe('true');
    expect(tab(fixture, 'Original').getAttribute('aria-selected')).toBe('false');
    expect(panelText(fixture)).toBe('Hallo Welt.');
    expect(fixture.componentInstance.wordCount()).toBe(2);
  });

  it('links tabs and panel for assistive technology', () => {
    const fixture = render(job(), [cleanupVariant()]);
    const panel: HTMLElement = fixture.nativeElement.querySelector('[role="tabpanel"]');
    const selected = tab(fixture, 'Original');

    expect(selected.getAttribute('aria-controls')).toBe(panel.id);
    expect(panel.getAttribute('aria-labelledby')).toBe(selected.id);
  });

  it('supports keyboard navigation with roving tabindex', () => {
    const fixture = render(job(), [cleanupVariant()]);
    const [original, edited] = tabs(fixture);
    expect([original.tabIndex, edited.tabIndex]).toEqual([0, -1]);

    press(fixture, original, 'ArrowRight');
    expect(panelText(fixture)).toBe('Hallo Welt.');
    expect(document.activeElement).toBe(tab(fixture, 'Bearbeitet'));
    expect(tabs(fixture).map((t) => t.tabIndex)).toEqual([-1, 0]);

    press(fixture, tab(fixture, 'Bearbeitet'), 'ArrowRight'); // wraps around
    expect(panelText(fixture)).toBe('aehm also hallo welt');

    press(fixture, tab(fixture, 'Original'), 'End');
    expect(panelText(fixture)).toBe('Hallo Welt.');

    press(fixture, tab(fixture, 'Bearbeitet'), 'Home');
    expect(panelText(fixture)).toBe('aehm also hallo welt');

    press(fixture, tab(fixture, 'Original'), 'ArrowLeft');
    expect(panelText(fixture)).toBe('Hallo Welt.');
  });

  it('copies and downloads the displayed version', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const createObjectURL = vi.fn().mockReturnValue('blob:test');
    Object.assign(window.URL, { createObjectURL, revokeObjectURL: vi.fn() });
    const fixture = render(job(), [cleanupVariant()]);

    tab(fixture, 'Bearbeitet').click();
    fixture.detectChanges();
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    buttons.find((b) => b.textContent?.includes('Kopieren'))!.click();
    buttons.find((b) => b.textContent?.includes('Download'))!.click();

    expect(writeText).toHaveBeenCalledWith('Hallo Welt.');
    const blob: Blob = createObjectURL.mock.calls[0][0];
    expect(await blob.text()).toBe('Hallo Welt.');
  });

  it('resets to the original version when another job is opened', () => {
    const fixture = render(job(), [cleanupVariant()]);
    tab(fixture, 'Bearbeitet').click();
    fixture.detectChanges();

    jobService.selectedJob.set(job({ id: 'job-2', rawTranscript: 'Zweiter Job.' }));
    jobService.variants.set([]);
    fixture.detectChanges();

    expect(panelText(fixture)).toBe('Zweiter Job.');
    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeNull();
  });

  it('translates the tab labels', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    const fixture = render(job(), [cleanupVariant()]);

    expect(tabs(fixture).map((t) => t.textContent?.trim())).toEqual(['Original', 'Edited']);
  });

  it('keeps the toggle usable when the raw transcript is empty', () => {
    const fixture = render(job({ rawTranscript: '' }), [cleanupVariant()]);

    tab(fixture, 'Original').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).not.toBeNull();
    expect(panelText(fixture)).toBe('');
  });
});
