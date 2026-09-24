import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { JobDetailComponent } from './job-detail.component';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJob, AudioJobStatus } from '../../models/audio-job.model';
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
    processedTranscript: 'Hallo Welt.',
    ...overrides,
  }) as AudioJob;

describe('JobDetailComponent transcript version toggle (S04-T4)', () => {
  let jobService: AudioJobService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [JobDetailComponent, translocoTesting('de')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    jobService = TestBed.inject(AudioJobService);
  });

  function render(value: AudioJob): ComponentFixture<JobDetailComponent> {
    const fixture = TestBed.createComponent(JobDetailComponent);
    jobService.selectedJob.set(value);
    fixture.detectChanges();
    return fixture;
  }

  const tabs = (fixture: ComponentFixture<JobDetailComponent>): HTMLButtonElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]'));
  const tab = (fixture: ComponentFixture<JobDetailComponent>, name: string) =>
    tabs(fixture).find((t) => t.textContent?.trim() === name)!;
  const panelText = (fixture: ComponentFixture<JobDetailComponent>) =>
    (fixture.nativeElement.querySelector('[role="tabpanel"]') as HTMLElement).textContent?.trim();
  const press = (
    fixture: ComponentFixture<JobDetailComponent>,
    target: HTMLElement,
    key: string,
  ) => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    fixture.detectChanges();
  };

  it('is not shown when there is no post-processed version', () => {
    const fixture = render(job({ processedTranscript: undefined }));

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('aehm also hallo welt');
  });

  it('offers both versions and selects the edited one by default', () => {
    const fixture = render(job());

    const tablist: HTMLElement = fixture.nativeElement.querySelector('[role="tablist"]');
    expect(tablist.getAttribute('aria-label')).toBe('Transkript-Fassung');
    expect(
      tabs(fixture).map((t) => [t.textContent?.trim(), t.getAttribute('aria-selected')]),
    ).toEqual([
      ['Bearbeitet', 'true'],
      ['Original', 'false'],
    ]);
    expect(panelText(fixture)).toBe('Hallo Welt.');
  });

  it('shows the raw transcript when "Original" is selected', () => {
    const fixture = render(job());

    tab(fixture, 'Original').click();
    fixture.detectChanges();

    expect(tab(fixture, 'Original').getAttribute('aria-selected')).toBe('true');
    expect(tab(fixture, 'Bearbeitet').getAttribute('aria-selected')).toBe('false');
    expect(panelText(fixture)).toBe('aehm also hallo welt');
    expect(fixture.componentInstance.wordCount()).toBe(4);
  });

  it('links tabs and panel for assistive technology', () => {
    const fixture = render(job());
    const panel: HTMLElement = fixture.nativeElement.querySelector('[role="tabpanel"]');
    const selected = tab(fixture, 'Bearbeitet');

    expect(selected.getAttribute('aria-controls')).toBe(panel.id);
    expect(panel.getAttribute('aria-labelledby')).toBe(selected.id);
  });

  it('supports keyboard navigation with roving tabindex', () => {
    const fixture = render(job());
    const [edited, original] = tabs(fixture);
    expect([edited.tabIndex, original.tabIndex]).toEqual([0, -1]);

    press(fixture, edited, 'ArrowRight');
    expect(panelText(fixture)).toBe('aehm also hallo welt');
    expect(document.activeElement).toBe(tab(fixture, 'Original'));
    expect(tabs(fixture).map((t) => t.tabIndex)).toEqual([-1, 0]);

    press(fixture, tab(fixture, 'Original'), 'ArrowRight'); // wraps around
    expect(panelText(fixture)).toBe('Hallo Welt.');

    press(fixture, tab(fixture, 'Bearbeitet'), 'End');
    expect(panelText(fixture)).toBe('aehm also hallo welt');

    press(fixture, tab(fixture, 'Original'), 'Home');
    expect(panelText(fixture)).toBe('Hallo Welt.');

    press(fixture, tab(fixture, 'Bearbeitet'), 'ArrowLeft');
    expect(panelText(fixture)).toBe('aehm also hallo welt');
  });

  it('copies and downloads the displayed version', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const createObjectURL = vi.fn().mockReturnValue('blob:test');
    Object.assign(window.URL, { createObjectURL, revokeObjectURL: vi.fn() });
    const fixture = render(job());

    tab(fixture, 'Original').click();
    fixture.detectChanges();
    const buttons: HTMLButtonElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    );
    buttons.find((b) => b.textContent?.includes('Kopieren'))!.click();
    buttons.find((b) => b.textContent?.includes('Download'))!.click();

    expect(writeText).toHaveBeenCalledWith('aehm also hallo welt');
    const blob: Blob = createObjectURL.mock.calls[0][0];
    expect(await blob.text()).toBe('aehm also hallo welt');
  });

  it('resets to the edited version when another job is opened', () => {
    const fixture = render(job());
    tab(fixture, 'Original').click();
    fixture.detectChanges();

    jobService.selectedJob.set(job({ id: 'job-2', processedTranscript: 'Zweiter Job.' }));
    fixture.detectChanges();

    expect(panelText(fixture)).toBe('Zweiter Job.');
  });

  it('translates the tab labels', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    const fixture = render(job());

    expect(tabs(fixture).map((t) => t.textContent?.trim())).toEqual(['Edited', 'Original']);
  });

  it('keeps the toggle usable when one version is empty', () => {
    const fixture = render(job({ rawTranscript: '' }));

    tab(fixture, 'Original').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).not.toBeNull();
    expect(panelText(fixture)).toBe('');
  });
});
