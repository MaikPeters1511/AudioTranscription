import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranscriptPlayerComponent } from './transcript-player.component';
import { translocoTesting } from '../../i18n/transloco-testing';
import { TranscriptSegment } from '../../models/audio-job.model';

const segments: TranscriptSegment[] = [
  { index: 0, startMs: 0, endMs: 1_500, text: 'Hallo zusammen.' },
  { index: 1, startMs: 1_500, endMs: 65_000, text: 'Los geht es.' },
];

describe('TranscriptPlayerComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render(response: TranscriptSegment[] = segments) {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(response);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    return { fixture, el, audio: el.querySelector('audio') };
  }

  /** jsdom does not play media; currentTime is a plain property here. */
  function fakeTime(audio: HTMLAudioElement, seconds = 0) {
    let time = seconds;
    Object.defineProperty(audio, 'currentTime', { get: () => time, set: (v: number) => (time = v), configurable: true });
  }

  it('plays the job audio and lists the segments with timestamps', () => {
    const { el, audio } = render();

    expect(audio!.getAttribute('src')).toBe('/api/audio-jobs/job-1/audio');
    expect(audio!.hasAttribute('controls')).toBe(true);
    const buttons = [...el.querySelectorAll<HTMLButtonElement>('ol button')];
    expect(buttons.map((b) => b.textContent!.replace(/\s+/g, ' ').trim())).toEqual([
      '0:00 Hallo zusammen.',
      '0:01 Los geht es.',
    ]);
  });

  it('marks the segment being played as current', () => {
    const { fixture, el, audio } = render();
    fakeTime(audio!, 2);

    audio!.dispatchEvent(new Event('timeupdate'));
    fixture.detectChanges();

    const buttons = el.querySelectorAll('ol button');
    expect(buttons[0].getAttribute('aria-current')).toBeNull();
    expect(buttons[1].getAttribute('aria-current')).toBe('true');
  });

  it('jumps to a segment on click', () => {
    const { fixture, el, audio } = render();
    fakeTime(audio!);

    el.querySelectorAll<HTMLButtonElement>('ol button')[1].click();
    fixture.detectChanges();

    expect(audio!.currentTime).toBe(1.5);
    expect(el.querySelectorAll('ol button')[1].getAttribute('aria-current')).toBe('true');
  });

  it('shows only the timed segments when the audio is gone', () => {
    const { fixture, el, audio } = render();

    audio!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(el.querySelector('audio')).toBeNull();
    expect(el.querySelectorAll('ol button').length).toBe(0);
    expect(el.textContent).toContain('0:01');
    expect(el.textContent).toContain('Los geht es.');
    expect(el.textContent).toContain('The audio file is no longer available');
  });

  it('explains when a job has no segments', () => {
    const { el } = render([]);

    expect(el.querySelector('audio')).toBeNull();
    expect(el.textContent).toContain('No timestamps are available for this transcript.');
  });
});

describe('TranscriptPlayerComponent speaker diarization (S11)', () => {
  let http: HttpTestingController;

  const diarizedSegments: TranscriptSegment[] = [
    { index: 0, startMs: 0, endMs: 1_500, text: 'Hallo zusammen.', speakerIndex: 0, speakerName: 'Anna' },
    { index: 1, startMs: 1_500, endMs: 3_000, text: "Los geht's.", speakerIndex: 1, speakerName: 'Sprecher 2' },
  ];
  const speakers = [{ index: 0, displayName: 'Anna' }, { index: 1, displayName: 'Sprecher 2' }];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(diarizedSegments);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush(speakers);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    return { fixture, el };
  }

  it('does not show a speaker toolbar for a non-diarized job', () => {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(segments);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid=speaker-toolbar]')).toBeNull();
  });

  it('prefixes each segment with its speaker name, not color alone', () => {
    const { el } = render();

    const items = [...el.querySelectorAll('ol li')].map((li) => li.textContent!.replace(/\s+/g, ' ').trim());
    expect(items[0]).toContain('Anna');
    expect(items[0]).toContain('Hallo zusammen.');
    expect(items[1]).toContain('Sprecher 2');
  });

  it('shows a badge per detected speaker with a rename control', () => {
    const { el } = render();

    const toolbar = el.querySelector('[data-testid=speaker-toolbar]')!;
    expect(toolbar.textContent).toContain('Anna');
    expect(toolbar.textContent).toContain('Sprecher 2');
    expect(toolbar.querySelectorAll('button[aria-label]').length).toBeGreaterThanOrEqual(2);
  });

  it('renames a speaker and reflects the new name in the toolbar and the segments', () => {
    const { fixture, el } = render();

    el.querySelectorAll<HTMLButtonElement>('[data-testid=speaker-toolbar] button')[0].click();
    fixture.detectChanges();

    const input: HTMLInputElement = el.querySelector('[data-testid=speaker-rename-input]')!;
    input.value = 'Ben';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid=speaker-save]')!.click();

    const req = http.expectOne('/api/audio-jobs/job-1/speakers/0');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ displayName: 'Ben' });
    req.flush(null);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid=speaker-toolbar]')!.textContent).toContain('Ben');
    expect(el.querySelector('[data-testid=speaker-toolbar]')!.textContent).not.toContain('Anna');
    const items = [...el.querySelectorAll('ol li')].map((li) => li.textContent!.replace(/\s+/g, ' ').trim());
    expect(items[0]).toContain('Ben');
  });
});

describe('TranscriptPlayerComponent semantics', () => {
  it('marks timestamps as machine-readable durations', () => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/audio-jobs/job-1/segments').flush(segments);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();

    const times = [...fixture.nativeElement.querySelectorAll('ol time')].map((t: Element) => t.getAttribute('datetime'));
    expect(times).toEqual(['PT0S', 'PT1.5S']);
  });
});
