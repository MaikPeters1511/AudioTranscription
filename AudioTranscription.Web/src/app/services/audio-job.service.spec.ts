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
