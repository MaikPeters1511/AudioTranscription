import { TestBed } from '@angular/core/testing';
import { AudioRecorderService } from './audio-recorder.service';

/** Minimal fake standing in for the browser's MediaRecorder (jsdom has none). */
class FakeMediaRecorder {
  static supportedTypes = new Set(['audio/webm;codecs=opus']);
  static isTypeSupported(type: string): boolean {
    return FakeMediaRecorder.supportedTypes.has(type);
  }
  static instances: FakeMediaRecorder[] = [];

  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: (() => void) | null = null;
  started = false;

  constructor(
    public stream: MediaStream,
    public options?: { mimeType?: string },
  ) {
    FakeMediaRecorder.instances.push(this);
  }

  start(): void {
    this.started = true;
  }

  stop(): void {
    this.ondataavailable?.({ data: new Blob(['chunk'], { type: this.options?.mimeType ?? 'audio/webm' }) });
    this.onstop?.();
  }
}

function fakeStream(): MediaStream {
  const track = { stop: vi.fn() } as unknown as MediaStreamTrack;
  return { getTracks: () => [track] } as unknown as MediaStream;
}

describe('AudioRecorderService', () => {
  let service: AudioRecorderService;
  let getUserMedia: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    FakeMediaRecorder.instances = [];
    (globalThis as any).MediaRecorder = FakeMediaRecorder;
    getUserMedia = vi.fn().mockResolvedValue(fakeStream());
    Object.defineProperty(globalThis.navigator, 'mediaDevices', {
      value: { getUserMedia },
      configurable: true,
    });

    TestBed.configureTestingModule({});
    service = TestBed.inject(AudioRecorderService);
  });

  it('starts idle', () => {
    expect(service.state()).toBe('idle');
  });

  it('requests the microphone and starts recording with the first supported mime type', async () => {
    await service.start();

    expect(getUserMedia).toHaveBeenCalledWith({ audio: true });
    expect(service.state()).toBe('recording');
    expect(service.mimeType()).toBe('audio/webm;codecs=opus');
    expect(FakeMediaRecorder.instances[0].started).toBe(true);
  });

  it('falls back to the next supported mime type', async () => {
    FakeMediaRecorder.supportedTypes = new Set(['audio/mp4']);

    await service.start();

    expect(service.mimeType()).toBe('audio/mp4');
  });

  it('reports permission-denied when getUserMedia is rejected', async () => {
    getUserMedia.mockRejectedValue(new DOMException('Denied', 'NotAllowedError'));

    await service.start();

    expect(service.state()).toBe('error');
    expect(service.errorKind()).toBe('permission-denied');
  });

  it('reports unsupported when the browser has no MediaRecorder', async () => {
    delete (globalThis as any).MediaRecorder;

    await service.start();

    expect(service.state()).toBe('error');
    expect(service.errorKind()).toBe('unsupported');
    expect(getUserMedia).not.toHaveBeenCalled();
  });

  it('produces a blob and stops the microphone tracks when stopped', async () => {
    await service.start();
    const track = FakeMediaRecorder.instances[0].stream.getTracks()[0];

    service.stop();

    expect(service.state()).toBe('stopped');
    expect(service.recordedBlob()).toBeInstanceOf(Blob);
    expect(track.stop).toHaveBeenCalled();
  });

  it('does nothing when stop is called while not recording', () => {
    service.stop();

    expect(service.state()).toBe('idle');
  });

  it('reset returns to idle and clears the recorded blob', async () => {
    await service.start();
    service.stop();

    service.reset();

    expect(service.state()).toBe('idle');
    expect(service.recordedBlob()).toBeNull();
    expect(service.errorKind()).toBeNull();
  });

  it('surfaces a recording-failed error from the underlying MediaRecorder', async () => {
    await service.start();

    FakeMediaRecorder.instances[0].onerror?.();

    expect(service.state()).toBe('error');
    expect(service.errorKind()).toBe('recording-failed');
  });
});
