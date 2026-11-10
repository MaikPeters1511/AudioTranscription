import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QueueService } from './queue.service';
import { AudioJobStatus } from '../models/audio-job.model';

const entry = (jobId: string, position: number, priority = 0) => ({
  jobId,
  fileName: `${jobId}.mp3`,
  status: position === 0 ? AudioJobStatus.Processing : AudioJobStatus.Pending,
  position,
  priority,
  estimatedWaitSeconds: null,
  estimatedProcessingSeconds: null,
});

describe('QueueService (S34)', () => {
  let service: QueueService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(QueueService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the queue overview', async () => {
    const loaded = service.load();
    http.expectOne('/api/queue').flush({ entries: [entry('a', 0), entry('b', 2)], totalWaiting: 3 });
    await loaded;

    expect(service.entries().map((e) => e.jobId)).toEqual(['a', 'b']);
    expect(service.totalWaiting()).toBe(3);
  });

  it('sends the new priority and reloads the queue', async () => {
    const changed = service.setPriority('b', 5);
    const put = http.expectOne('/api/audio-jobs/b/priority');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ priority: 5 });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    http.expectOne('/api/queue').flush({ entries: [entry('b', 1, 5)], totalWaiting: 1 });
    await changed;

    expect(service.entries()[0].priority).toBe(5);
  });

  it('leaves a failed priority change to the caller and keeps the list', async () => {
    service.entries.set([entry('b', 1)]);

    const changed = service.setPriority('b', 5);
    http.expectOne('/api/audio-jobs/b/priority').flush({}, { status: 409, statusText: 'Conflict' });

    await expect(changed).rejects.toBeDefined();
    expect(service.entries()).toHaveLength(1);
  });

  it('cancels a job and reloads the queue', async () => {
    const cancelled = service.cancel('b');
    const post = http.expectOne('/api/audio-jobs/b/cancel');
    expect(post.request.method).toBe('POST');
    post.flush(null, { status: 202, statusText: 'Accepted' });
    await Promise.resolve();
    http.expectOne('/api/queue').flush({ entries: [], totalWaiting: 0 });
    await cancelled;

    expect(service.entries()).toEqual([]);
  });
});
