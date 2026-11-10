import { Page, Request, Route } from '@playwright/test';

/**
 * In-memory fake of the API for the `*.mocked.spec.ts` flows (S31). It only needs the Angular dev
 * server: every /api and /hubs request is answered here, so these tests check the UI flows without
 * a backend. The tests against the real stack live in the `*.real.spec.ts` files.
 */
export interface FakeJob {
  id: string;
  fileName: string;
  status: 0 | 1 | 2 | 3 | 4;
  rawTranscript?: string;
  language?: string;
  durationSeconds?: number;
  progressPercent?: number;
  segments: { index: number; startMs: number; endMs: number; text: string; speakerIndex?: number; version?: number }[];
  /** Corrections per segment index (S32), oldest first; version 0 is the original text. */
  /** Chapters of the job (S25); POST /chapters fills them with one chapter per two segments. */
  chapters?: { id: string; position: number; startMs: number; endMs: number; title: string; summary: string }[];
  edits?: Record<number, { version: number; text: string }[]>;
  speakers: { index: number; displayName: string }[];
}

export interface MockApiOptions {
  /** Email the fake login accepts. */
  email: string;
  /** Password the fake login accepts. */
  password: string;
}

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

const empty = (route: Route, status = 204) => route.fulfill({ status });

export class MockApi {
  readonly jobs = new Map<string, FakeJob>();
  /** The signed-in user's tags (S20); tag ids per job and favorite jobs are kept per fake job id. */
  readonly tags: { id: string; name: string; color?: string | null }[] = [];
  readonly jobTags = new Map<string, string[]>();
  readonly favorites = new Set<string>();
  /** Jobs waiting in the transcription queue (S34), in arrival order. */
  readonly queue: { jobId: string; priority: number }[] = [];
  /** Resumable (tus) uploads in progress (S27); the job exists once all bytes arrived. */
  readonly tusUploads = new Map<string, { fileName: string; length: number; offset: number; jobId?: string }>();
  /** Which PATCH request (1-based, counted over all tus uploads) loses its connection, to simulate a network drop. */
  dropTusPatchNumber?: number;
  private tusPatches = 0;
  signedIn = false;
  /** Every request the UI sent, as `METHOD path`, to assert on what was (not) called. */
  readonly requests: string[] = [];
  private nextId = 1;

  constructor(
    private readonly page: Page,
    private readonly options: MockApiOptions,
  ) {}

  /** Adds a pending job to the queue, as an upload would. */
  queueJob(fileName: string): FakeJob {
    const job = this.addJob({ fileName, status: 0 });
    this.queue.push({ jobId: job.id, priority: 0 });
    return job;
  }

  addJob(job: Partial<FakeJob> & Pick<FakeJob, 'fileName'>): FakeJob {
    const id = job.id ?? `00000000-0000-4000-8000-${String(this.nextId++).padStart(12, '0')}`;
    const full: FakeJob = {
      id,
      status: 2,
      segments: [],
      speakers: [],
      ...job,
    };
    this.jobs.set(id, full);
    return full;
  }

  async install(): Promise<void> {
    // Nothing real may be reached: unknown API calls fail loudly instead of hitting a dev proxy
    await this.page.route(/\/(api|hubs)\//, (route) => this.handle(route));
  }

  private organization(job: FakeJob) {
    return {
      tags: (this.jobTags.get(job.id) ?? []).map((id) => this.tags.find((t) => t.id === id)).filter(Boolean),
      isFavorite: this.favorites.has(job.id),
      folderId: null,
    };
  }

  private listItem(job: FakeJob) {
    return {
      ...this.organization(job),
      id: job.id,
      fileName: job.fileName,
      fileSizeBytes: 2048,
      status: job.status,
      language: job.language,
      durationSeconds: job.durationSeconds,
      createdAtUtc: '2026-01-01T10:00:00Z',
      completedAtUtc: job.status === 2 ? '2026-01-01T10:01:00Z' : undefined,
      progressPercent: job.progressPercent,
    };
  }

  /** Minimal tus server: creation, offset query, chunk append and the job lookup of a finished upload. */
  private async handleTus(route: Route, request: Request, path: string, method: string) {
    const tusHeaders = { 'Tus-Resumable': '1.0.0', 'Cache-Control': 'no-store' };
    const parts = path.split('/').filter(Boolean); // api, uploads, id?, job?
    const id = parts[2];

    if (!id && method === 'POST') {
      const metadata = new Map(
        (request.headers()['upload-metadata'] ?? '').split(',').map((pair) => {
          const [key, value] = pair.trim().split(' ');
          return [key, Buffer.from(value ?? '', 'base64').toString('utf-8')] as const;
        }),
      );
      const uploadId = `tus${this.nextId++}`;
      this.tusUploads.set(uploadId, {
        fileName: metadata.get('filename') ?? 'upload',
        length: Number(request.headers()['upload-length']),
        offset: 0,
      });
      const origin = new URL(request.url()).origin;
      return route.fulfill({ status: 201, headers: { ...tusHeaders, Location: `${origin}/api/uploads/${uploadId}` } });
    }

    const upload = id ? this.tusUploads.get(id) : undefined;
    if (!upload) return route.fulfill({ status: 404, headers: tusHeaders });

    if (parts[3] === 'job' && method === 'GET') {
      return upload.jobId ? json(route, { id: upload.jobId }) : json(route, { title: 'Upload incomplete' }, 409);
    }
    if (method === 'HEAD') {
      return route.fulfill({
        status: 200,
        headers: { ...tusHeaders, 'Upload-Offset': String(upload.offset), 'Upload-Length': String(upload.length) },
      });
    }
    if (method === 'PATCH') {
      this.tusPatches++;
      if (this.tusPatches === this.dropTusPatchNumber) return route.abort('connectionreset');
      if (Number(request.headers()['upload-offset']) !== upload.offset) return route.fulfill({ status: 409, headers: tusHeaders });
      upload.offset += request.postDataBuffer()?.length ?? 0;
      if (upload.offset >= upload.length) {
        upload.jobId = this.addJob({ fileName: upload.fileName, status: 2, rawTranscript: 'Hallo Welt vom Upload', language: 'de' }).id;
      }
      return route.fulfill({ status: 204, headers: { ...tusHeaders, 'Upload-Offset': String(upload.offset) } });
    }
    return route.fulfill({ status: 204, headers: tusHeaders });
  }

  private detail(job: FakeJob) {
    return {
      ...this.listItem(job),
      contentType: 'audio/mpeg',
      rawTranscript: job.rawTranscript,
      model: 'Base',
      diarizationRequested: job.speakers.length > 0,
    };
  }

  private async handle(route: Route): Promise<void> {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    this.requests.push(`${method} ${path}`);

    if (path.startsWith('/hubs/')) {
      // No live connection: the UI falls back to loading data directly
      return route.abort();
    }

    if (method === 'POST' && path === '/api/auth/login') {
      const body = request.postDataJSON() as { email: string; password: string };
      if (body.email === this.options.email && body.password === this.options.password) {
        this.signedIn = true;
        return json(route, {});
      }
      return route.fulfill({ status: 401 });
    }
    if (method === 'POST' && path === '/api/auth/logout') {
      this.signedIn = false;
      return empty(route);
    }
    if (path === '/api/auth/me') {
      return this.signedIn ? json(route, { email: this.options.email, roles: [] }) : route.fulfill({ status: 401 });
    }
    if (!this.signedIn) {
      return route.fulfill({ status: 401 });
    }

    if (path === '/api/transcription-options') {
      return json(route, {
        models: ['Base'],
        defaultModel: 'Base',
        languages: ['de', 'en'],
        postProcessingEnabled: false,
        diarizationEnabled: false,
      });
    }

    if (path === '/api/search') {
      return this.search(route, url.searchParams.get('q') ?? '');
    }

    if (path === '/api/tags' && method === 'GET') return json(route, this.tags);
    if (path === '/api/tags' && method === 'POST') {
      const body = request.postDataJSON() as { name: string; color?: string };
      const tag = { id: `tag-${this.tags.length + 1}`, name: body.name, color: body.color ?? null };
      this.tags.push(tag);
      return json(route, tag, 201);
    }
    if (path === '/api/folders' && method === 'GET') return json(route, []);
    if (path === '/api/glossaries' && method === 'GET') return json(route, []);
    if (path === '/api/queue' && method === 'GET') {
      // Highest priority first, arrival order among equals (Array.sort is stable)
      const waiting = [...this.queue].sort((a, b) => b.priority - a.priority);
      const entries = waiting.map((q, index) => ({
        jobId: q.jobId,
        fileName: this.jobs.get(q.jobId)?.fileName,
        status: 0,
        position: index + 1,
        priority: q.priority,
        estimatedWaitSeconds: null,
        estimatedProcessingSeconds: null,
      }));
      return json(route, { entries, totalWaiting: entries.length });
    }

    if (path.startsWith('/api/uploads')) return this.handleTus(route, request, path, method);

    if (path === '/api/audio-jobs' && method === 'GET') {
      const tag = url.searchParams.get('tag');
      const favorite = url.searchParams.get('favorite') === 'true';
      const items = [...this.jobs.values()]
        .filter((j) => !tag || (this.jobTags.get(j.id) ?? []).includes(tag))
        .filter((j) => !favorite || this.favorites.has(j.id))
        .map((j) => this.listItem(j));
      return json(route, { items, totalCount: items.length, page: 1, pageSize: 20 });
    }
    if (path === '/api/audio-jobs' && method === 'POST') {
      const job = this.addJob({ fileName: 'uploaded.mp3', status: 2, rawTranscript: 'Hallo Welt vom Upload', language: 'de' });
      return json(route, { id: job.id }, 201);
    }

    const match = path.match(/^\/api\/audio-jobs\/([^/]+)(?:\/(.+))?$/);
    const job = match ? this.jobs.get(match[1]) : undefined;
    if (match && !job) {
      return route.fulfill({ status: 404 });
    }
    if (match && job) {
      const sub = match[2];
      if (!sub && method === 'GET') return json(route, this.detail(job));
      if (!sub && method === 'DELETE') {
        this.jobs.delete(job.id);
        return empty(route);
      }
      if (sub === 'segments') {
        return json(
          route,
          job.segments.map((s) => ({
            ...s,
            speakerName: s.speakerIndex === undefined ? undefined : job.speakers.find((p) => p.index === s.speakerIndex)?.displayName,
          })),
        );
      }
      if (sub === 'chapters' && method === 'GET') {
        return json(route, { status: job.chapters?.length ? 'Completed' : 'None', chapters: job.chapters ?? [] });
      }
      if (sub === 'chapters' && method === 'POST') {
        job.chapters = [];
        for (let i = 0; i < job.segments.length; i += 2) {
          const pair = job.segments.slice(i, i + 2);
          job.chapters.push({
            id: `chapter-${job.chapters.length}`,
            position: job.chapters.length,
            startMs: pair[0].startMs,
            endMs: pair[pair.length - 1].endMs,
            title: `Thema ${job.chapters.length + 1}`,
            summary: pair.map((p) => p.text).join(' '),
          });
        }
        return json(route, { status: 'Completed', chapters: job.chapters }, 202);
      }
      const segmentEdit = sub?.match(/^segments\/(\d+)(?:\/(history|restore))?$/);
      if (segmentEdit) {
        const segment = job.segments.find((s) => s.index === Number(segmentEdit[1]));
        if (!segment) return json(route, { title: 'Not found' }, 404);
        job.edits ??= {};
        const edits = (job.edits[segment.index] ??= [{ version: 0, text: segment.text }]);
        if (segmentEdit[2] === 'history' && method === 'GET') return json(route, [...edits].reverse());
        const body = request.postDataJSON() as { text?: string; version?: number; expectedVersion: number };
        if ((segment.version ?? 0) !== body.expectedVersion) return json(route, { title: 'Conflict' }, 409);
        const text = segmentEdit[2] === 'restore' ? edits.find((e) => e.version === body.version)?.text : body.text;
        if (text === undefined) return json(route, { title: 'Not found' }, 404);
        segment.version = (segment.version ?? 0) + 1;
        segment.text = text;
        edits.push({ version: segment.version, text });
        return json(route, segment);
      }
      if (sub === 'speakers' && method === 'GET') return json(route, job.speakers);
      const rename = sub?.match(/^speakers\/(\d+)$/);
      if (rename && method === 'PUT') {
        const speaker = job.speakers.find((s) => s.index === Number(rename[1]));
        if (speaker) speaker.displayName = (request.postDataJSON() as { displayName: string }).displayName;
        return empty(route);
      }
      if (sub === 'priority' && method === 'PUT') {
        const entry = this.queue.find((q) => q.jobId === job.id);
        if (!entry) return json(route, { title: 'Conflict' }, 409);
        entry.priority = (request.postDataJSON() as { priority: number }).priority;
        return empty(route);
      }
      if (sub === 'cancel' && method === 'POST') {
        const index = this.queue.findIndex((q) => q.jobId === job.id);
        if (index >= 0) this.queue.splice(index, 1);
        job.status = 4;
        return empty(route, 202);
      }
      if (sub === 'variants') return json(route, []);
      if (sub === 'tags' && method === 'PUT') {
        this.jobTags.set(job.id, (request.postDataJSON() as { tagIds: string[] }).tagIds);
        return empty(route);
      }
      if (sub === 'favorite') {
        if (method === 'PUT') this.favorites.add(job.id);
        else this.favorites.delete(job.id);
        return empty(route);
      }
      if (sub === 'audio') return route.fulfill({ status: 404 });
    }

    // Anything else is a gap in this fake, make it visible instead of silently passing
    return json(route, { error: `Unmocked ${method} ${path}` }, 501);
  }

  private async search(route: Route, query: string): Promise<void> {
    const needle = query.trim().toLowerCase();
    const items = [...this.jobs.values()].flatMap((job) =>
      job.segments
        .filter((s) => needle && s.text.toLowerCase().includes(needle))
        .map((s) => ({
          jobId: job.id,
          fileName: job.fileName,
          snippet: s.text,
          highlightStart: s.text.toLowerCase().indexOf(needle),
          highlightLength: needle.length,
          segmentStartMs: s.startMs,
        })),
    );
    return json(route, { items, totalCount: items.length, page: 1, pageSize: 20 });
  }
}
