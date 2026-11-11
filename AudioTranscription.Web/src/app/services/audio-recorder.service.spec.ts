import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from '../auth/auth.service';
import { AudioRecorderService } from './audio-recorder.service';
import { InMemoryRecordingStore } from './in-memory-recording-store';
import { RECORDING_STORE } from './recording-store.provider';

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
  timeslice?: number;

  constructor(
    public stream: MediaStream,
    public options?: { mimeType?: string },
  ) {
    FakeMediaRecorder.instances.push(this);
  }

  start(timeslice?: number): void {
    this.started = true;
    this.timeslice = timeslice;
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
  let store: InMemoryRecordingStore;

  beforeEach(() => {
    FakeMediaRecorder.instances = [];
    FakeMediaRecorder.supportedTypes = new Set(['audio/webm;codecs=opus']);
    (globalThis as any).MediaRecorder = FakeMediaRecorder;
    getUserMedia = vi.fn().mockResolvedValue(fakeStream());
    Object.defineProperty(globalThis.navigator, 'mediaDevices', {
      value: { getUserMedia },
      configurable: true,
    });

    store = new InMemoryRecordingStore();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: RECORDING_STORE, useValue: store }],
    });
    vi.spyOn(TestBed.inject(AuthService), 'user').mockReturnValue({ email: 'alice@example.com' });
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
  // --- S33: sections are kept in the local store while recording -----------------------------------

  it('asks the recorder for a section every few seconds', async () => {
    await service.start();

    expect(FakeMediaRecorder.instances[0].timeslice).toBe(5000);
  });

  it('opens a stored recording for the signed-in user', async () => {
    await service.start();

    const [stored] = await store.list();
    expect(stored).toMatchObject({ userEmail: 'alice@example.com', mimeType: 'audio/webm;codecs=opus', status: 'recording' });
    expect(service.recordingId()).toBe(stored.id);
  });

  it('stores every section as it arrives and marks the recording as a draft when stopped', async () => {
    await service.start();
    const fake = FakeMediaRecorder.instances[0];

    fake.ondataavailable?.({ data: new Blob(['abc']) });
    fake.ondataavailable?.({ data: new Blob(['de']) });
    await service.flush();
    expect((await store.list())[0]).toMatchObject({ chunkCount: 2, sizeBytes: 5, status: 'recording' });

    service.stop(); // the fake adds one last section
    await service.flush();
    expect((await store.list())[0]).toMatchObject({ chunkCount: 3, status: 'draft' });
    expect(service.storageWarning()).toBeNull();
  });

  it('stops cleanly and keeps the take when the storage is full', async () => {
    await service.start();
    const fake = FakeMediaRecorder.instances[0];
    fake.ondataavailable?.({ data: new Blob(['first']) });
    await service.flush();
    vi.spyOn(store, 'appendChunk').mockRejectedValue(new DOMException('full', 'QuotaExceededError'));

    fake.ondataavailable?.({ data: new Blob(['second']) });
    await service.flush();

    expect(service.storageWarning()).toBe('storage-full');
    expect(service.state()).toBe('stopped');
    expect(service.recordedBlob()).toBeInstanceOf(Blob);
    expect(service.recordedBlob()!.size).toBeGreaterThan(5); // nothing recorded so far is lost
  });

  it('keeps recording but warns when the local store cannot be used at all', async () => {
    vi.spyOn(store, 'create').mockRejectedValue(new Error('IndexedDB unavailable'));

    await service.start();

    expect(service.state()).toBe('recording');
    expect(service.recordingId()).toBeNull();
    expect(service.storageWarning()).toBe('storage-failed');
  });

  it('deletes the stored take when it is discarded', async () => {
    await service.start();
    service.stop();
    await service.flush();

    service.reset();
    await service.flush();

    expect(await store.list()).toEqual([]);
    expect(service.recordingId()).toBeNull();
  });

  it('keeps the stored take when it is handed over to the upload queue', async () => {
    await service.start();
    service.stop();
    await service.flush();

    service.reset({ keepStored: true });
    await service.flush();

    expect(await store.list()).toHaveLength(1);
    expect(service.recordingId()).toBeNull();
  });

  it('asks the browser to keep the local data', async () => {
    const persist = vi.fn().mockResolvedValue(true);
    Object.defineProperty(globalThis.navigator, 'storage', { value: { persist }, configurable: true });

    await service.start();

    expect(persist).toHaveBeenCalled();
  });
});
