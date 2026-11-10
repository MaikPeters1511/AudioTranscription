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

  afterEach(() => {
    // The chapter bar (S25) asks for its chapters once the segments are shown; these tests do not look at them
    http.match('/api/audio-jobs/job-1/chapters').forEach((r) => r.flush({ status: 'None', chapters: [] }));
    http.verify();
  });

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

  afterEach(() => {
    // The chapter bar (S25) asks for its chapters once the segments are shown; these tests do not look at them
    http.match('/api/audio-jobs/job-1/chapters').forEach((r) => r.flush({ status: 'None', chapters: [] }));
    http.verify();
  });

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

describe('TranscriptPlayerComponent search deep-link (S13)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // The chapter bar (S25) asks for its chapters once the segments are shown; these tests do not look at them
    http.match('/api/audio-jobs/job-1/chapters').forEach((r) => r.flush({ status: 'None', chapters: [] }));
    http.verify();
  });

  it('jumps to the given segment once segments have loaded', () => {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.componentRef.setInput('initialSeekMs', 1_500);
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(segments);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('ol button');
    expect(buttons[1].getAttribute('aria-current')).toBe('true');
  });

  it('does nothing when no seek time is given', () => {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(segments);
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();

    // Without a deep-link, playback position stays at 0 (the default), which is segment 0 itself
    const buttons = fixture.nativeElement.querySelectorAll('ol button');
    expect(buttons[0].getAttribute('aria-current')).toBe('true');
    expect(buttons[1].getAttribute('aria-current')).toBeNull();
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

describe('TranscriptPlayerComponent transcript editor (S32)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranscriptPlayerComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // The chapter bar (S25) asks for its chapters once the segments are shown; these tests do not look at them
    http.match('/api/audio-jobs/job-1/chapters').forEach((r) => r.flush({ status: 'None', chapters: [] }));
    http.verify();
  });

  function render(canEdit = true) {
    const fixture = TestBed.createComponent(TranscriptPlayerComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments').flush(segments.map((s) => ({ ...s, version: 0 })));
    http.expectOne('/api/audio-jobs/job-1/speakers').flush([]);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    return { fixture, el };
  }

  const editButtons = (el: HTMLElement) => el.querySelectorAll<HTMLButtonElement>('[data-testid="segment-edit"]');
  const textarea = (el: HTMLElement) => el.querySelector<HTMLTextAreaElement>('[data-testid="segment-editor"]');

  function type(fixture: { detectChanges(): void }, area: HTMLTextAreaElement, value: string) {
    area.value = value;
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('offers no editing to readers of a shared job', () => {
    const { el } = render(false);

    expect(editButtons(el)).toHaveLength(0);
  });

  it('opens an editor with the current text via the edit button', () => {
    const { fixture, el } = render();

    editButtons(el)[0].click();
    fixture.detectChanges();

    expect(textarea(el)?.value).toBe('Hallo zusammen.');
  });

  it('opens the editor on Enter and on double-click of a segment', () => {
    const { fixture, el } = render();
    const rows = el.querySelectorAll<HTMLElement>('[data-testid="segment-text"]');

    rows[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(textarea(el)?.value).toBe('Hallo zusammen.');

    el.querySelectorAll<HTMLElement>('[data-testid="segment-text"]')[0].dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    el.querySelector<HTMLButtonElement>('[data-testid="editor-cancel"]')!.click();
    fixture.detectChanges();
    expect(textarea(el)).toBeNull();
    el.querySelectorAll<HTMLElement>('[data-testid="segment-text"]')[1].dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    fixture.detectChanges();
    expect(textarea(el)?.value).toBe('Los geht es.');
  });

  it('saves a correction with the expected version and shows the new text', () => {
    const { fixture, el } = render();
    editButtons(el)[0].click();
    fixture.detectChanges();

    type(fixture, textarea(el)!, 'Hallo Welt.');
    el.querySelector<HTMLButtonElement>('[data-testid="editor-save"]')!.click();

    const req = http.expectOne('/api/audio-jobs/job-1/segments/0');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ text: 'Hallo Welt.', expectedVersion: 0 });
    req.flush({ index: 0, startMs: 0, endMs: 1_500, text: 'Hallo Welt.', version: 1 });
    fixture.detectChanges();

    expect(textarea(el)).toBeNull();
    expect(el.textContent).toContain('Hallo Welt.');
  });

  it('saves with Ctrl+Enter and cancels with Escape', () => {
    const { fixture, el } = render();
    editButtons(el)[0].click();
    fixture.detectChanges();
    type(fixture, textarea(el)!, 'Neu');

    textarea(el)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true }));
    http.expectOne('/api/audio-jobs/job-1/segments/0').flush({ index: 0, startMs: 0, endMs: 1_500, text: 'Neu', version: 1 });
    fixture.detectChanges();
    expect(textarea(el)).toBeNull();

    editButtons(el)[1].click();
    fixture.detectChanges();
    textarea(el)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(textarea(el)).toBeNull();
  });

  it('does not save an unchanged text', () => {
    const { fixture, el } = render();
    editButtons(el)[0].click();
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('[data-testid="editor-save"]')!.disabled).toBe(true);
  });

  it('keeps the draft and explains a conflict', () => {
    const { fixture, el } = render();
    editButtons(el)[0].click();
    fixture.detectChanges();
    type(fixture, textarea(el)!, 'Meine Korrektur');

    el.querySelector<HTMLButtonElement>('[data-testid="editor-save"]')!.click();
    http.expectOne('/api/audio-jobs/job-1/segments/0').flush({}, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('changed in the meantime');
    expect(textarea(el)?.value).toBe('Meine Korrektur');
  });

  it('shows a clean error without technical details when saving fails', () => {
    const { fixture, el } = render();
    editButtons(el)[0].click();
    fixture.detectChanges();
    type(fixture, textarea(el)!, 'Neu');

    el.querySelector<HTMLButtonElement>('[data-testid="editor-save"]')!.click();
    http.expectOne('/api/audio-jobs/job-1/segments/0').flush('SqlException at line 12', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const alert = el.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain('could not be saved');
    expect(alert.textContent).not.toContain('SqlException');
  });

  it('plays the segment from the editor with Alt+P', () => {
    const { fixture, el } = render();
    const audio = el.querySelector('audio')!;
    let time = 0;
    Object.defineProperty(audio, 'currentTime', { get: () => time, set: (v: number) => (time = v), configurable: true });
    audio.play = vi.fn().mockResolvedValue(undefined);
    editButtons(el)[1].click();
    fixture.detectChanges();

    textarea(el)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'p', altKey: true, bubbles: true }));

    expect(time).toBe(1.5);
    expect(audio.play).toHaveBeenCalled();
  });

  it('shows the version history of a segment and applies a restore', () => {
    const { fixture, el } = render();

    el.querySelectorAll<HTMLButtonElement>('[data-testid="segment-history-toggle"]')[0].click();
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments/0/history').flush([
      { version: 1, text: 'Hallo Welt.', editedBy: 'u', editedAtUtc: '2026-11-10T10:00:00Z' },
      { version: 0, text: 'Hallo zusammen.' },
    ]);
    fixture.detectChanges();
    expect(el.querySelector('app-segment-history')).not.toBeNull();
  });
});
