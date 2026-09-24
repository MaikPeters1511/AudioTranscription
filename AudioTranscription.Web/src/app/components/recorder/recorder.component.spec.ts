import { TestBed } from '@angular/core/testing';
import { RecorderComponent } from './recorder.component';
import { AudioRecorderService } from '../../services/audio-recorder.service';
import { translocoTesting } from '../../i18n/transloco-testing';

function fakeRecorder(): AudioRecorderService {
  const recorder = new AudioRecorderService();
  vi.spyOn(recorder, 'start').mockImplementation(async () => {
    recorder.errorKind.set(null);
    recorder.recordedBlob.set(null);
    recorder.state.set('recording');
  });
  vi.spyOn(recorder, 'stop').mockImplementation(() => {
    recorder.mimeType.set('audio/webm');
    recorder.recordedBlob.set(new Blob(['take'], { type: 'audio/webm' }));
    recorder.state.set('stopped');
  });
  vi.spyOn(recorder, 'reset').mockImplementation(() => {
    recorder.recordedBlob.set(null);
    recorder.errorKind.set(null);
    recorder.state.set('idle');
  });
  return recorder;
}

describe('RecorderComponent', () => {
  let recorder: AudioRecorderService;

  beforeEach(() => {
    vi.useFakeTimers();
    (globalThis.URL as any).createObjectURL = vi.fn(() => 'blob:fake-url');
    (globalThis.URL as any).revokeObjectURL = vi.fn();

    recorder = fakeRecorder();
    TestBed.configureTestingModule({
      imports: [RecorderComponent, translocoTesting('en')],
      providers: [{ provide: AudioRecorderService, useValue: recorder }],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function render() {
    const fixture = TestBed.createComponent(RecorderComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('starts idle with a start button that is not pressed', () => {
    const { el } = render();

    const button = el.querySelector('button[aria-pressed]')!;
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(el.textContent).toContain('Start recording');
  });

  it('starts recording on click, marks the button pressed and counts elapsed time', async () => {
    const { fixture, el } = render();

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    const button = el.querySelector('button[aria-pressed]')!;
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(recorder.start).toHaveBeenCalled();

    vi.advanceTimersByTime(3000);
    fixture.detectChanges();
    expect(el.querySelector('time')!.textContent).toBe('0:03');
  });

  it('is fully keyboard operable via the native button', async () => {
    const { fixture, el } = render();
    const button = el.querySelector<HTMLButtonElement>('button[aria-pressed]')!;

    button.focus();
    expect(document.activeElement).toBe(button);
    button.click(); // native buttons activate on Enter/Space the same as click
    await Promise.resolve();
    fixture.detectChanges();

    expect(recorder.start).toHaveBeenCalled();
  });

  it('announces state changes in an aria-live region', async () => {
    const { fixture, el } = render();

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    const live = el.querySelector('[aria-live]')!;
    expect(live.textContent).toContain('Recording…');
  });

  it('shows a preview and transcribe/discard actions after stopping', async () => {
    const { fixture, el } = render();
    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    fixture.detectChanges();

    expect(el.querySelector('audio')?.getAttribute('src')).toBe('blob:fake-url');
    expect(el.textContent).toContain('Transcribe');
    expect(el.textContent).toContain('Discard');
  });

  it('emits a File when transcribe is clicked and resets the recorder', async () => {
    const { fixture, el } = render();
    let emitted: File | undefined;
    fixture.componentInstance.recorded.subscribe((file: File) => (emitted = file));

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click(); // stop
    fixture.detectChanges();

    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Transcribe'))!.click();

    expect(emitted).toBeInstanceOf(File);
    expect(emitted!.name).toBe('recording.webm');
    expect(recorder.reset).toHaveBeenCalled();
  });

  it('discards the take without emitting', async () => {
    const { fixture, el } = render();
    let emitted = false;
    fixture.componentInstance.recorded.subscribe(() => (emitted = true));

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click(); // stop
    fixture.detectChanges();

    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Discard'))!.click();
    fixture.detectChanges();

    expect(emitted).toBe(false);
    expect(recorder.reset).toHaveBeenCalled();
    expect(el.querySelector('audio')).toBeNull();
  });

  it('shows a translated error when permission is denied', async () => {
    vi.spyOn(recorder, 'start').mockImplementation(async () => {
      recorder.errorKind.set('permission-denied');
      recorder.state.set('error');
    });
    const { fixture, el } = render();

    el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    expect(el.textContent).toContain('Microphone access was denied');
  });
});
