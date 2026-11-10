import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChapterService } from './chapter.service';
import { Chapter, ChapterList } from '../models/audio-job.model';

const chapter = (id: string, position: number, title = `Kapitel ${position}`): Chapter => ({
  id,
  position,
  startMs: position * 10_000,
  endMs: (position + 1) * 10_000,
  title,
  summary: '',
});

describe('ChapterService (S25)', () => {
  let service: ChapterService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ChapterService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the chapters of a job', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Completed', chapters: [chapter('a', 0)] });

    expect(service.state().status).toBe('Completed');
    expect(service.state().chapters).toHaveLength(1);
  });

  it('shows no chapters when they cannot be loaded', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({}, { status: 500, statusText: 'Server Error' });

    expect(service.state()).toEqual({ status: 'None', chapters: [] });
  });

  it('ignores the answer for a job that is no longer open', () => {
    service.load('job-1');
    const stale = http.expectOne('/api/audio-jobs/job-1/chapters');
    service.load('job-2');
    http.expectOne('/api/audio-jobs/job-2/chapters').flush({ status: 'Completed', chapters: [chapter('b', 0)] });

    stale.flush({ status: 'Completed', chapters: [chapter('a', 0)] });

    expect(service.state().chapters[0].id).toBe('b');
  });

  it('starts the generation and shows the pending state', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'None', chapters: [] });

    service.generate('job-1').subscribe();
    const req = http.expectOne('/api/audio-jobs/job-1/chapters');
    expect(req.request.method).toBe('POST');
    req.flush({ status: 'Pending', chapters: [] });

    expect(service.state().status).toBe('Pending');
  });

  it('reloads when the generation is done, but only for the open job', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Pending', chapters: [] });

    service.refresh('other-job');
    http.expectNone('/api/audio-jobs/other-job/chapters');

    service.refresh('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Completed', chapters: [chapter('a', 0)] });
    expect(service.state().status).toBe('Completed');
  });

  it('updates title and summary of a chapter in place', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Completed', chapters: [chapter('a', 0), chapter('b', 1)] } satisfies ChapterList);

    service.update('job-1', chapter('b', 1), 'Neu', 'Text').subscribe();
    const req = http.expectOne('/api/audio-jobs/job-1/chapters/b');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ title: 'Neu', summary: 'Text' });
    req.flush({ ...chapter('b', 1), title: 'Neu', summary: 'Text' });

    expect(service.state().chapters.map((c) => c.title)).toEqual(['Kapitel 0', 'Neu']);
  });

  it('deletes a chapter and reloads the list', () => {
    service.load('job-1');
    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Completed', chapters: [chapter('a', 0), chapter('b', 1)] });

    service.delete('job-1', chapter('a', 0)).subscribe();
    const req = http.expectOne('/api/audio-jobs/job-1/chapters/a');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    http.expectOne('/api/audio-jobs/job-1/chapters').flush({ status: 'Completed', chapters: [{ ...chapter('b', 0), startMs: 0, endMs: 20_000 }] });
    expect(service.state().chapters).toHaveLength(1);
  });
});
