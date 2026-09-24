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

describe('TranscriptPlayerComponent semantics', () => {
  it('marks timestamps as machine-readable durations', () => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/audio-jobs/job-1/segments').flush(segments);
    fixture.detectChanges();

    const times = [...fixture.nativeElement.querySelectorAll('ol time')].map((t: Element) => t.getAttribute('datetime'));
    expect(times).toEqual(['PT0S', 'PT1.5S']);
  });
});
