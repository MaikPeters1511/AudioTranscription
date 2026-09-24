import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AudioJobService } from './audio-job.service';
import {
  AudioJob,
  AudioJobListItem,
  AudioJobStatus,
  PaginatedResult,
} from '../models/audio-job.model';

const listItem = (id: string, status = AudioJobStatus.Pending): AudioJobListItem =>
  ({
    id,
    fileName: `${id}.mp3`,
    fileSizeBytes: 1024,
    status,
    createdAtUtc: '2026-09-24T10:00:00Z',
  }) as AudioJobListItem;

describe('AudioJobService', () => {
  let service: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads a page of jobs and updates the state', () => {
    service.loadJobs(2);
    expect(service.loading()).toBe(true);

    const req = http.expectOne((r) => r.url === '/api/audio-jobs');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({
      items: [listItem('a')],
      totalCount: 21,
      page: 2,
      pageSize: 20,
    } as PaginatedResult<AudioJobListItem>);

    expect(service.jobs().map((j) => j.id)).toEqual(['a']);
    expect(service.totalCount()).toBe(21);
    expect(service.totalPages()).toBe(2);
    expect(service.loading()).toBe(false);
  });

  it('resets the loading flag when loading jobs fails', () => {
    service.loadJobs();

    http
      .expectOne((r) => r.url === '/api/audio-jobs')
      .flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.loading()).toBe(false);
  });

  it('keeps the current job visible while reloading it silently', () => {
    const job = { id: 'x', rawTranscript: 'alt' } as AudioJob;
    service.selectedJob.set(job);

    service.loadJob('x', true);
    expect(service.selectedJob()).toBe(job);

    http.expectOne('/api/audio-jobs/x').flush({ ...job, rawTranscript: 'neu' });
    expect(service.selectedJob()?.rawTranscript).toBe('neu');
  });

  it('replaces an existing job in the list and prepends new ones', () => {
    service.jobs.set([listItem('a'), listItem('b')]);

    service.updateJobInList(listItem('b', AudioJobStatus.Completed));
    service.updateJobInList(listItem('c'));

    expect(service.jobs().map((j) => [j.id, j.status])).toEqual([
      ['c', AudioJobStatus.Pending],
      ['a', AudioJobStatus.Pending],
      ['b', AudioJobStatus.Completed],
    ]);
  });
});

describe('AudioJobService job actions (S09)', () => {
  let service: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('deletes a job and removes it from the list', async () => {
    service.jobs.set([listItem('a'), listItem('b')]);
    service.totalCount.set(2);

    const result = service.deleteJob('a');
    const req = http.expectOne('/api/audio-jobs/a');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await result;

    expect(service.jobs().map((j) => j.id)).toEqual(['b']);
    expect(service.totalCount()).toBe(1);
  });

  it.each([
    ['cancel', AudioJobStatus.Cancelled],
    ['retry', AudioJobStatus.Pending],
  ] as const)('%s refreshes its state without waiting for SignalR', async (action, newStatus) => {
    service.jobs.set([listItem('a', AudioJobStatus.Processing)]);
    service.selectedJob.set({ id: 'a', status: AudioJobStatus.Processing } as AudioJob);

    const result = action === 'cancel' ? service.cancelJob('a') : service.retryJob('a');
    const req = http.expectOne(`/api/audio-jobs/a/${action}`);
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 202, statusText: 'Accepted' });
    await Promise.resolve();
    http.expectOne('/api/audio-jobs/a').flush({ ...listItem('a', newStatus), contentType: 'audio/mpeg' });
    await result;

    expect(service.jobs()[0].status).toBe(newStatus);
    expect(service.selectedJob()?.status).toBe(newStatus);
  });

  it('propagates API errors to the caller', async () => {
    const retry = service.retryJob('a');
    http.expectOne('/api/audio-jobs/a/retry').flush(null, { status: 410, statusText: 'Gone' });

    await expect(retry).rejects.toMatchObject({ status: 410 });
  });

  it('clears the open job when it is removed', () => {
    service.selectedJob.set({ id: 'a' } as AudioJob);
    service.jobs.set([listItem('a')]);

    service.removeJobFromList('a');

    expect(service.selectedJob()).toBeNull();
    expect(service.jobs()).toEqual([]);
  });

  it('ignores removal of unknown jobs', () => {
    service.jobs.set([listItem('a')]);
    service.totalCount.set(1);

    service.removeJobFromList('zzz');

    expect(service.jobs()).toHaveLength(1);
    expect(service.totalCount()).toBe(1);
  });
});

describe('AudioJobService transcription settings (S08)', () => {
  let service: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const file = () => new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' });

  it('sends the chosen model and language with the upload', () => {
    service.uploadFile(file(), { model: 'Small', language: 'de' }).subscribe();

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect((body.get('file') as File).name).toBe('meeting.mp3');
    expect(body.get('model')).toBe('Small');
    expect(body.get('language')).toBe('de');
  });

  it('leaves model and language to the server when none are chosen', () => {
    service.uploadFile(file()).subscribe();

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect(body.has('model')).toBe(false);
    expect(body.has('language')).toBe(false);
  });

  it('loads the selectable models and languages', () => {
    let result: unknown;
    service.loadTranscriptionOptions().subscribe((o) => (result = o));

    const options = { models: ['Base', 'Small'], defaultModel: 'Base', languages: ['de', 'en'] };
    http.expectOne('/api/transcription-options').flush(options);

    expect(result).toEqual(options);
  });
});

describe('AudioJobService progress (S07)', () => {
  let service: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  it('takes the progress of running jobs from the loaded list', () => {
    service.loadJobs();
    http.expectOne((r) => r.url === '/api/audio-jobs').flush({
      items: [{ ...listItem('a', AudioJobStatus.Processing), progressPercent: 30 }, listItem('b', AudioJobStatus.Pending)],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });

    expect(service.progressOf('a')).toBe(30);
    expect(service.progressOf('b')).toBeUndefined();
  });

  it('forgets the progress once the job is no longer processing', () => {
    service.setProgress('a', 80);

    service.updateJobInList(listItem('a', AudioJobStatus.Completed));

    expect(service.progressOf('a')).toBeUndefined();
  });

  it('ignores a lower value arriving late', () => {
    service.setProgress('a', 60);
    service.setProgress('a', 55);

    expect(service.progressOf('a')).toBe(60);
  });
});
