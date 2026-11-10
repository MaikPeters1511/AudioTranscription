import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OrganizeService } from './organize.service';
import { AudioJobService } from './audio-job.service';
import { AudioJobListItem, AudioJobStatus } from '../models/audio-job.model';

const job = (id: string, extra: Partial<AudioJobListItem> = {}): AudioJobListItem =>
  ({ id, fileName: `${id}.mp3`, fileSizeBytes: 1, status: AudioJobStatus.Completed, createdAtUtc: '2026-01-01T00:00:00Z', ...extra }) as AudioJobListItem;

describe('OrganizeService (S20)', () => {
  let service: OrganizeService;
  let jobs: AudioJobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OrganizeService);
    jobs = TestBed.inject(AudioJobService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const flushLoad = async (tags: unknown[], folders: unknown[]) => {
    const loaded = service.load();
    http.expectOne('/api/tags').flush(tags);
    http.expectOne('/api/folders').flush(folders);
    await loaded;
  };

  it('loads the tags and folders', async () => {
    await flushLoad([{ id: 't1', name: 'Budget' }], [{ id: 'f1', name: 'Projekte' }]);

    expect(service.tags().map((t) => t.name)).toEqual(['Budget']);
    expect(service.folders().map((f) => f.name)).toEqual(['Projekte']);
  });

  it('flattens the folder tree depth-first with the siblings sorted by name', async () => {
    await flushLoad([], [
      { id: 'b', name: 'B' },
      { id: 'a', name: 'A' },
      { id: 'a2', name: 'Zweig', parentId: 'a' },
      { id: 'a1', name: 'Ast', parentId: 'a' },
      { id: 'lost', name: 'Verwaist', parentId: 'unknown' },
    ]);

    expect(service.folderOptions().map((o) => `${o.depth}:${o.name}`)).toEqual(['0:A', '1:Ast', '1:Zweig', '0:B', '0:Verwaist']);
  });

  it('creates a tag and keeps the list sorted', async () => {
    service.tags.set([{ id: 't2', name: 'Zeta' }]);

    const created = service.createTag('Alpha', '#ff0000');
    const req = http.expectOne('/api/tags');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Alpha', color: '#ff0000' });
    req.flush({ id: 't1', name: 'Alpha', color: '#ff0000' });
    await created;

    expect(service.tags().map((t) => t.name)).toEqual(['Alpha', 'Zeta']);
  });

  it('deleting a tag removes it from the jobs and from an active tag filter', async () => {
    service.tags.set([{ id: 't1', name: 'Budget' }]);
    jobs.jobs.set([job('a', { tags: [{ id: 't1', name: 'Budget' }, { id: 't2', name: 'Rest' }] })]);
    jobs.filters.set({ tag: 't1' });

    const deleted = service.deleteTag('t1');
    http.expectOne('/api/tags/t1').flush(null, { status: 204, statusText: 'No Content' });
    await deleted;
    // the filter on a vanished tag is dropped, which reloads the list
    http.expectOne((r) => r.url === '/api/audio-jobs').flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });

    expect(service.tags()).toEqual([]);
    expect(jobs.filters().tag).toBeUndefined();
  });

  it('sets the tags of a job and shows them in the list at once', async () => {
    service.tags.set([{ id: 't1', name: 'Budget' }, { id: 't2', name: 'Eilig' }]);
    jobs.jobs.set([job('a')]);

    const done = service.setJobTags('a', ['t2']);
    const req = http.expectOne('/api/audio-jobs/a/tags');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ tagIds: ['t2'] });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(jobs.jobs()[0].tags?.map((t) => t.name)).toEqual(['Eilig']);
  });

  it('marks and unmarks a favorite, and refreshes a favorites-only list', async () => {
    jobs.jobs.set([job('a', { isFavorite: false })]);

    const mark = service.setFavorite('a', true);
    const put = http.expectOne('/api/audio-jobs/a/favorite');
    expect(put.request.method).toBe('PUT');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await mark;
    expect(jobs.jobs()[0].isFavorite).toBe(true);

    jobs.filters.set({ favorite: true });
    const unmark = service.setFavorite('a', false);
    const del = http.expectOne('/api/audio-jobs/a/favorite');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await unmark;
    // on the favorites list the job has to vanish: the list is reloaded
    http.expectOne((r) => r.url === '/api/audio-jobs' && r.params.get('favorite') === 'true').flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('files a job in a folder, or at the top level with null', async () => {
    jobs.jobs.set([job('a')]);

    const filed = service.setJobFolder('a', 'f1');
    const req = http.expectOne('/api/audio-jobs/a/folder');
    expect(req.request.body).toEqual({ folderId: 'f1' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await filed;
    expect(jobs.jobs()[0].folderId).toBe('f1');

    const unfiled = service.setJobFolder('a', null);
    http.expectOne('/api/audio-jobs/a/folder').flush(null, { status: 204, statusText: 'No Content' });
    await unfiled;
    expect(jobs.jobs()[0].folderId).toBeNull();
  });

  it('creates, renames/moves and deletes a folder (reloading the tree after a delete)', async () => {
    const created = service.createFolder('Neu', 'f1');
    const post = http.expectOne('/api/folders');
    expect(post.request.body).toEqual({ name: 'Neu', parentId: 'f1' });
    post.flush({ id: 'f2', name: 'Neu', parentId: 'f1' });
    await created;
    expect(service.folders().map((f) => f.id)).toEqual(['f2']);

    const updated = service.updateFolder('f2', 'Neuer', null);
    const put = http.expectOne('/api/folders/f2');
    expect(put.request.body).toEqual({ name: 'Neuer', parentId: null });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await updated;
    expect(service.folders()[0]).toMatchObject({ name: 'Neuer', parentId: null });

    const deleted = service.deleteFolder('f2');
    http.expectOne('/api/folders/f2').flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    http.expectOne('/api/folders').flush([]);
    await deleted;
    expect(service.folders()).toEqual([]);
  });
});
