import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Subscription } from 'rxjs';
import {
  RESUMABLE_UPLOAD_THRESHOLD_BYTES,
  ResumableUploadService,
  UploadControl,
  isResumableUpload,
} from './resumable-upload.service';

interface FakeUploadOptions {
  endpoint: string;
  chunkSize: number;
  retryDelays: number[];
  metadata: Record<string, string>;
  onProgress: (sent: number, total: number) => void;
  onSuccess: () => void;
  onError: (error: Error) => void;
}

const hoisted = vi.hoisted(() => {
  const instances: FakeUploadShape[] = [];
  class FakeUpload {
    url: string | null = 'http://localhost/api/uploads/abc123';
    started = 0;
    aborted: boolean[] = [];
    resumedFrom: unknown[] = [];
    previous: unknown[] = [];
    constructor(
      public file: File,
      public options: FakeUploadOptions,
    ) {
      instances.push(this);
    }
    findPreviousUploads() {
      return Promise.resolve(this.previous);
    }
    resumeFromPreviousUpload(previous: unknown) {
      this.resumedFrom.push(previous);
    }
    start() {
      this.started++;
    }
    abort(terminate?: boolean) {
      this.aborted.push(!!terminate);
      return Promise.resolve();
    }
  }
  return { instances, FakeUpload };
});

interface FakeUploadShape {
  url: string | null;
  started: number;
  aborted: boolean[];
  resumedFrom: unknown[];
  previous: unknown[];
  options: FakeUploadOptions;
}

const instances = hoisted.instances;

vi.mock('tus-js-client', () => ({ Upload: hoisted.FakeUpload }));

const bigFile = (name = 'meeting.mp3') => new File([new Uint8Array(8)], name, { type: 'audio/mpeg' });

describe('isResumableUpload', () => {
  it('uses the resumable path only from the size threshold on', () => {
    expect(isResumableUpload({ size: RESUMABLE_UPLOAD_THRESHOLD_BYTES - 1 } as File)).toBe(false);
    expect(isResumableUpload({ size: RESUMABLE_UPLOAD_THRESHOLD_BYTES } as File)).toBe(true);
  });
});

describe('UploadControl', () => {
  it('pauses and resumes the attached upload', () => {
    const control = new UploadControl();
    const target = { pause: vi.fn(), resume: vi.fn() };
    control.attach(target);

    control.pause();
    expect(control.paused()).toBe(true);
    expect(target.pause).toHaveBeenCalled();

    control.resume();
    expect(control.paused()).toBe(false);
    expect(target.resume).toHaveBeenCalled();
  });

  it('does nothing without an upload', () => {
    const control = new UploadControl();

    control.pause();

    expect(control.paused()).toBe(false);
    expect(control.resumable()).toBe(false);
  });
});

describe('ResumableUploadService', () => {
  let service: ResumableUploadService;
  let http: HttpTestingController;

  beforeEach(() => {
    instances.length = 0;
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ResumableUploadService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** The tus client looks for earlier uploads asynchronously before it starts. */
  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('uploads in chunks with retries and the settings as metadata', async () => {
    service.upload(bigFile(), { model: 'Base', language: 'de', diarize: true, glossaryId: 'g1' }).subscribe();
    await settle();

    const upload = instances[0];
    expect(upload.started).toBe(1);
    expect(upload.options.endpoint).toBe('/api/uploads');
    expect(upload.options.chunkSize).toBe(5 * 1024 * 1024);
    expect(upload.options.retryDelays.length).toBeGreaterThan(3);
    expect(upload.options.metadata).toEqual({
      filename: 'meeting.mp3',
      filetype: 'audio/mpeg',
      model: 'Base',
      language: 'de',
      diarize: 'true',
      glossaryId: 'g1',
    });
  });

  it('leaves out settings that were not chosen', async () => {
    service.upload(bigFile(), {}).subscribe();
    await settle();

    expect(instances[0].options.metadata).toEqual({ filename: 'meeting.mp3', filetype: 'audio/mpeg' });
  });

  it('reports the progress in percent', async () => {
    const events: { progress: number; jobId?: string }[] = [];
    service.upload(bigFile(), {}).subscribe((e) => events.push(e));
    await settle();

    instances[0].options.onProgress(60, 100);

    expect(events).toEqual([{ progress: 60 }]);
  });

  it('asks for the job once the last chunk is through and finishes with its id', async () => {
    const events: { progress: number; jobId?: string }[] = [];
    let completed = false;
    service.upload(bigFile(), {}).subscribe({ next: (e) => events.push(e), complete: () => (completed = true) });
    await settle();

    instances[0].options.onSuccess();

    http.expectOne('/api/uploads/abc123/job').flush({ id: 'job-9' });
    expect(events).toEqual([{ progress: 100, jobId: 'job-9' }]);
    expect(completed).toBe(true);
  });

  it('reports a rejection of the finished file like a failed request', async () => {
    let error: { error?: { detail?: string } } | undefined;
    service.upload(bigFile(), {}).subscribe({ error: (e) => (error = e) });
    await settle();

    instances[0].options.onSuccess();
    http.expectOne('/api/uploads/abc123/job').flush({ title: 'Upload rejected', detail: 'Wrong content' }, { status: 422, statusText: 'x' });

    expect(error?.error?.detail).toBe('Wrong content');
  });

  it('turns a server message into the error detail', async () => {
    let error: { error?: { detail?: string } } | undefined;
    service.upload(bigFile(), {}).subscribe({ error: (e) => (error = e) });
    await settle();

    instances[0].options.onError(
      Object.assign(new Error('tus: unexpected response'), {
        originalResponse: { getStatus: () => 413, getBody: () => 'File size exceeds the maximum allowed size (500 bytes).' },
      }),
    );

    expect(error?.error?.detail).toBe('File size exceeds the maximum allowed size (500 bytes).');
  });

  it('does not leak technical error text when the server sent no message', async () => {
    let error: { error?: { detail?: string } } | undefined;
    service.upload(bigFile(), {}).subscribe({ error: (e) => (error = e) });
    await settle();

    instances[0].options.onError(new Error('tus: failed to upload chunk at offset 0, caused by TypeError: Failed to fetch'));

    expect(error?.error?.detail).toBeUndefined();
  });

  it('resumes a matching earlier upload instead of starting from zero', async () => {
    const previous = { uploadUrl: 'http://localhost/api/uploads/old' };
    vi.spyOn(hoisted.FakeUpload.prototype, 'findPreviousUploads').mockResolvedValue([previous]);
    service.upload(bigFile(), {}).subscribe();
    await settle();

    expect(instances[0].resumedFrom).toEqual([previous]);
    expect(instances[0].started).toBe(1);
  });

  it('terminates the server session when the user cancels', async () => {
    const subscription: Subscription = service.upload(bigFile(), {}).subscribe();
    await settle();

    subscription.unsubscribe();

    expect(instances[0].aborted).toEqual([true]);
  });

  it('keeps a finished upload when the subscription ends', async () => {
    const subscription = service.upload(bigFile(), {}).subscribe();
    await settle();
    instances[0].options.onSuccess();
    http.expectOne('/api/uploads/abc123/job').flush({ id: 'job-9' });

    subscription.unsubscribe();

    expect(instances[0].aborted).toEqual([]);
  });

  it('can be paused and resumed through the control without ending the upload', async () => {
    const control = new UploadControl();
    service.upload(bigFile(), {}, control).subscribe();
    await settle();

    expect(control.resumable()).toBe(true);
    control.pause();
    expect(instances[0].aborted).toEqual([false]);

    control.resume();
    expect(instances[0].started).toBe(2);
  });
});
