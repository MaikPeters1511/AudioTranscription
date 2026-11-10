import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { translocoTesting } from '../i18n/transloco-testing';
import { AudioJobService } from './audio-job.service';
import { ConnectivityService } from './connectivity.service';
import { InMemoryRecordingStore } from './in-memory-recording-store';
import { OFFLINE_RECOVERY_IDLE_MS, OfflineUploadQueueService } from './offline-upload-queue.service';
import { RECORDING_STORE } from './recording-store.provider';
import { ToastService } from './toast.service';

describe('OfflineUploadQueueService', () => {
  let store: InMemoryRecordingStore;
  let online: ReturnType<typeof signal<boolean>>;
  let user: ReturnType<typeof signal<{ email: string } | null>>;
  let uploads: { file: File; events: Subject<{ progress: number; jobId?: string }> }[];
  let uploadFile: ReturnType<typeof vi.fn>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    store = new InMemoryRecordingStore();
    online = signal(true);
    user = signal<{ email: string } | null>({ email: 'alice@example.com' });
    uploads = [];
    uploadFile = vi.fn((file: File) => {
      const events = new Subject<{ progress: number; jobId?: string }>();
      uploads.push({ file, events });
      return events.asObservable();
    });
    toast = { success: vi.fn(), error: vi.fn() };

    TestBed.configureTestingModule({
      imports: [translocoTesting('en')],
      providers: [
        { provide: RECORDING_STORE, useValue: store },
        { provide: ConnectivityService, useValue: { online: online.asReadonly() } },
        { provide: AuthService, useValue: { user: user.asReadonly() } },
        { provide: AudioJobService, useValue: { uploadFile } },
        { provide: ToastService, useValue: toast },
      ],
    });
  });

  /** A stopped take ready to upload; `status` lets a test start from any state. */
  async function addRecording(options: { status?: 'draft' | 'pending' | 'failed' | 'recording' | 'uploading'; email?: string; mimeType?: string; text?: string } = {}) {
    const recording = await store.create(options.email ?? 'alice@example.com', options.mimeType ?? 'audio/webm');
    await store.appendChunk(recording.id, new Blob([options.text ?? 'audio']));
    await store.setStatus(recording.id, options.status ?? 'pending');
    return recording.id;
  }

  async function start() {
    const queue = TestBed.inject(OfflineUploadQueueService);
    TestBed.tick();
    await vi.waitFor(() => expect(queue.ready()).toBe(true));
    return queue;
  }

  const statusOf = async (id: string) => (await store.list()).find((r) => r.id === id)?.status;

  it('lists only the recordings of the signed-in user', async () => {
    const mine = await addRecording();
    await addRecording({ email: 'bob@example.com' });
    online.set(false);

    const queue = await start();

    expect(queue.items().map((r) => r.id)).toEqual([mine]);
    expect(queue.count()).toBe(1);
  });

  it('never uploads the recordings of another user', async () => {
    await addRecording({ email: 'bob@example.com' });

    await start();

    expect(uploadFile).not.toHaveBeenCalled();
  });

  it('turns a crashed or abandoned take into a pending upload', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    try {
      vi.setSystemTime(new Date('2026-11-10T10:00:00Z'));
      const crashed = await addRecording({ status: 'recording' });
      const stopped = await addRecording({ status: 'draft' });
      vi.setSystemTime(new Date('2026-11-10T10:00:00Z').getTime() + OFFLINE_RECOVERY_IDLE_MS + 1000);
      const running = await addRecording({ status: 'recording' }); // still being written to
      online.set(false);

      await start();

      expect(await statusOf(crashed)).toBe('pending');
      expect(await statusOf(stopped)).toBe('pending');
      expect(await statusOf(running)).toBe('recording');
    } finally {
      vi.useRealTimers();
    }
  });

  it('picks up a take that was cut off just now once it has been idle long enough', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout'] });
    try {
      const id = await addRecording({ status: 'recording' }); // e.g. the tab was reloaded during recording
      online.set(false);
      await start();
      expect(await statusOf(id)).toBe('recording');

      await vi.advanceTimersByTimeAsync(OFFLINE_RECOVERY_IDLE_MS + 2000);

      await vi.waitFor(async () => expect(await statusOf(id)).toBe('pending'));
    } finally {
      vi.useRealTimers();
    }
  });

  it('uploads a pending recording as a file once online and deletes it afterwards', async () => {
    const id = await addRecording({ mimeType: 'audio/webm;codecs=opus', text: 'take' });

    const queue = await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    expect(uploads[0].file.name).toMatch(/^recording-.*\.webm$/);
    expect(uploads[0].file.type).toBe('audio/webm;codecs=opus');
    expect(await statusOf(id)).toBe('uploading');

    uploads[0].events.next({ progress: 40 });
    expect(queue.progress().get(id)).toBe(40);

    uploads[0].events.next({ progress: 100, jobId: 'job-1' });
    uploads[0].events.complete();
    await vi.waitFor(async () => expect(await store.list()).toEqual([]));
    expect(queue.items()).toEqual([]);
    expect(toast.success).toHaveBeenCalled();
  });

  it('names an MP4 recording accordingly', async () => {
    await addRecording({ mimeType: 'audio/mp4' });

    await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalled());

    expect(uploads[0].file.name).toMatch(/\.mp4$/);
  });

  it('waits for the connection and then uploads by itself', async () => {
    online.set(false);
    await addRecording();
    await start();
    expect(uploadFile).not.toHaveBeenCalled();

    online.set(true);
    TestBed.tick();

    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));
  });

  it('does not upload without a signed-in user and continues after the next sign-in', async () => {
    user.set(null);
    await addRecording();
    await start();
    expect(uploadFile).not.toHaveBeenCalled();

    user.set({ email: 'alice@example.com' });
    TestBed.tick();

    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));
  });

  it('uploads one recording after the other', async () => {
    await addRecording();
    await addRecording();

    await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    uploads[0].events.next({ progress: 100, jobId: 'a' });
    uploads[0].events.complete();

    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(2));
  });

  it('keeps a recording that failed on the network and retries it when the connection returns', async () => {
    const id = await addRecording();
    const queue = await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    uploads[0].events.error(new HttpErrorResponse({ status: 0 }));
    await vi.waitFor(async () => expect(await statusOf(id)).toBe('failed'));
    expect(queue.items()[0].lastError).toBe('network');
    expect(uploadFile).toHaveBeenCalledTimes(1); // no retry loop while the connection state is unchanged

    online.set(false);
    TestBed.tick();
    online.set(true);
    TestBed.tick();

    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(2));
  });

  it('keeps a recording the server rejected, shows why and uploads again only on request', async () => {
    const id = await addRecording();
    const queue = await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    uploads[0].events.error(new HttpErrorResponse({ status: 413, error: { detail: 'File too large' } }));
    await vi.waitFor(async () => expect(await statusOf(id)).toBe('failed'));
    expect(queue.items()[0].lastError).toBe('File too large');

    online.set(false);
    TestBed.tick();
    online.set(true);
    TestBed.tick();
    await Promise.resolve();
    expect(uploadFile).toHaveBeenCalledTimes(1);

    await queue.retry(id);
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(2));
  });

  it('keeps the recording pending when the session has expired', async () => {
    const id = await addRecording();
    await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    user.set(null); // the interceptor signs the user out on a 401
    uploads[0].events.error(new HttpErrorResponse({ status: 401 }));

    await vi.waitFor(async () => expect(await statusOf(id)).toBe('pending'));
    expect(uploadFile).toHaveBeenCalledTimes(1);
  });

  it('queues a stopped take and starts uploading it', async () => {
    const id = await addRecording({ status: 'draft' });
    online.set(false);
    const queue = await start(); // recovery runs once at start and is not what is tested here
    await store.setStatus(id, 'draft');

    online.set(true);
    await queue.enqueue(id);

    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));
  });

  it('removes a waiting recording', async () => {
    online.set(false);
    const id = await addRecording();
    const queue = await start();

    await queue.remove(id);

    expect(await store.list()).toEqual([]);
    expect(queue.items()).toEqual([]);
  });

  it('cancels a running upload when its recording is removed', async () => {
    const id = await addRecording();
    const queue = await start();
    await vi.waitFor(() => expect(uploadFile).toHaveBeenCalledTimes(1));

    await queue.remove(id);

    expect(uploads[0].events.observed).toBe(false);
    expect(await store.list()).toEqual([]);
  });

  it('deletes all recordings of the current user, and only theirs', async () => {
    online.set(false);
    await addRecording();
    await addRecording({ status: 'failed' });
    const others = await addRecording({ email: 'bob@example.com' });
    const queue = await start();

    await queue.discardAllOfCurrentUser();

    expect((await store.list()).map((r) => r.id)).toEqual([others]);
    expect(queue.items()).toEqual([]);
  });
});
