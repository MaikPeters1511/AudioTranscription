import { Blob as NodeBlob } from 'node:buffer';
import { IDBFactory } from 'fake-indexeddb';
import { InMemoryRecordingStore } from './in-memory-recording-store';
import { IndexedDbRecordingStore } from './indexeddb-recording-store';
import { RecordingStore, isQuotaError } from './recording-store';
import { createRecordingStore } from './recording-store.provider';

async function textOf(blob: Blob | null): Promise<string> {
  return blob ? blob.text() : '';
}

// jsdom's Blob cannot be structured-cloned, which IndexedDB does with every stored value; a real browser can
const jsdomBlob = globalThis.Blob;
beforeAll(() => {
  globalThis.Blob = NodeBlob as unknown as typeof Blob;
});
afterAll(() => {
  globalThis.Blob = jsdomBlob;
});

/** Both implementations have to behave the same way; the in-memory one is the fallback without IndexedDB. */
const implementations: [string, () => RecordingStore][] = [
  ['IndexedDbRecordingStore', () => new IndexedDbRecordingStore(new IDBFactory())],
  ['InMemoryRecordingStore', () => new InMemoryRecordingStore()],
];

describe.each(implementations)('%s', (_name, createStore) => {
  let store: RecordingStore;

  beforeEach(() => {
    store = createStore();
  });

  it('creates a recording in state "recording" for its owner', async () => {
    const recording = await store.create('alice@example.com', 'audio/webm');

    expect(recording).toMatchObject({
      userEmail: 'alice@example.com',
      mimeType: 'audio/webm',
      status: 'recording',
      sizeBytes: 0,
      chunkCount: 0,
    });
    expect(recording.id).toBeTruthy();
    expect(await store.list()).toEqual([recording]);
  });

  it('appends sections and joins them in order', async () => {
    const { id } = await store.create('alice@example.com', 'audio/webm');

    await store.appendChunk(id, new Blob(['one-']));
    await store.appendChunk(id, new Blob(['two-']));
    await store.appendChunk(id, new Blob(['three']));

    const joined = await store.assemble(id);
    expect(joined?.type).toBe('audio/webm');
    expect(await textOf(joined)).toBe('one-two-three');
    const [recording] = await store.list();
    expect(recording.chunkCount).toBe(3);
    expect(recording.sizeBytes).toBe(13);
  });

  it('changes the status and remembers the last error', async () => {
    const { id } = await store.create('alice@example.com', 'audio/webm');

    await store.setStatus(id, 'failed', 'network down');

    expect((await store.list())[0]).toMatchObject({ status: 'failed', lastError: 'network down' });

    await store.setStatus(id, 'pending');
    expect((await store.list())[0].lastError).toBeUndefined();
  });

  it('refreshes the last-write time with every section and status change', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    try {
      vi.setSystemTime(new Date('2026-11-10T10:00:00Z'));
      const { id } = await store.create('alice@example.com', 'audio/webm');
      vi.setSystemTime(new Date('2026-11-10T10:00:30Z'));
      await store.appendChunk(id, new Blob(['x']));

      expect((await store.list())[0].updatedAtUtc).toBe('2026-11-10T10:00:30.000Z');
    } finally {
      vi.useRealTimers();
    }
  });

  it('deletes a recording together with its sections', async () => {
    const first = await store.create('alice@example.com', 'audio/webm');
    const second = await store.create('alice@example.com', 'audio/webm');
    await store.appendChunk(first.id, new Blob(['a']));
    await store.appendChunk(second.id, new Blob(['b']));

    await store.delete(first.id);

    expect((await store.list()).map((r) => r.id)).toEqual([second.id]);
    expect(await store.assemble(first.id)).toBeNull();
    expect(await textOf(await store.assemble(second.id))).toBe('b');
  });

  it('rejects appending to an unknown recording', async () => {
    await expect(store.appendChunk('missing', new Blob(['x']))).rejects.toThrow();
  });
});

describe('IndexedDbRecordingStore persistence', () => {
  it('keeps the data for a new store instance on the same database (reload after a crash)', async () => {
    const factory = new IDBFactory();
    const first = new IndexedDbRecordingStore(factory);
    const { id } = await first.create('alice@example.com', 'audio/webm');
    await first.appendChunk(id, new Blob(['saved']));

    const afterReload = new IndexedDbRecordingStore(factory);

    expect(await textOf(await afterReload.assemble(id))).toBe('saved');
    expect((await afterReload.list())[0].status).toBe('recording');
  });
});

describe('createRecordingStore', () => {
  it('falls back to memory without IndexedDB', () => {
    expect(createRecordingStore(undefined)).toBeInstanceOf(InMemoryRecordingStore);
    expect(createRecordingStore(new IDBFactory())).toBeInstanceOf(IndexedDbRecordingStore);
  });
});

describe('isQuotaError', () => {
  it('recognizes the quota error of the browser', () => {
    expect(isQuotaError(new DOMException('full', 'QuotaExceededError'))).toBe(true);
    expect(isQuotaError(new DOMException('other', 'AbortError'))).toBe(false);
    expect(isQuotaError(new Error('x'))).toBe(false);
  });
});
