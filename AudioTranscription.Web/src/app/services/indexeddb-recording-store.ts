import { RecordingStatus, RecordingStore, StoredRecording } from './recording-store';

const DATABASE_NAME = 'audio-transcription-offline';
const DATABASE_VERSION = 1;
const RECORDINGS = 'recordings';
const CHUNKS = 'chunks';

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function transactionDone(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error);
    transaction.onabort = () => reject(transaction.error ?? new DOMException('Transaction aborted', 'AbortError'));
  });
}

/**
 * Recordings in IndexedDB (S33, ADR 0012): metadata in "recordings", every section as its own Blob in
 * "chunks" (key: [recordingId, index]), so a section is written without touching the earlier ones.
 */
export class IndexedDbRecordingStore extends RecordingStore {
  private database?: Promise<IDBDatabase>;

  constructor(private readonly factory: IDBFactory) {
    super();
  }

  async create(userEmail: string, mimeType: string): Promise<StoredRecording> {
    const now = new Date().toISOString();
    const recording: StoredRecording = {
      id: crypto.randomUUID(),
      userEmail,
      mimeType,
      createdAtUtc: now,
      updatedAtUtc: now,
      status: 'recording',
      sizeBytes: 0,
      chunkCount: 0,
    };
    const transaction = (await this.open()).transaction(RECORDINGS, 'readwrite');
    transaction.objectStore(RECORDINGS).put(recording);
    await transactionDone(transaction);
    return recording;
  }

  async appendChunk(id: string, chunk: Blob): Promise<void> {
    const transaction = (await this.open()).transaction([RECORDINGS, CHUNKS], 'readwrite');
    const recordings = transaction.objectStore(RECORDINGS);
    const done = transactionDone(transaction);
    const recording = await requestResult<StoredRecording | undefined>(recordings.get(id));
    if (!recording) {
      transaction.abort();
      await done.catch(() => undefined);
      throw new Error(`Recording ${id} does not exist`);
    }
    transaction.objectStore(CHUNKS).put(chunk, [id, recording.chunkCount]);
    recordings.put({
      ...recording,
      chunkCount: recording.chunkCount + 1,
      sizeBytes: recording.sizeBytes + chunk.size,
      updatedAtUtc: new Date().toISOString(),
    });
    await done;
  }

  async setStatus(id: string, status: RecordingStatus, lastError?: string): Promise<void> {
    const transaction = (await this.open()).transaction(RECORDINGS, 'readwrite');
    const recordings = transaction.objectStore(RECORDINGS);
    const done = transactionDone(transaction);
    const recording = await requestResult<StoredRecording | undefined>(recordings.get(id));
    if (recording) {
      const updated: StoredRecording = { ...recording, status, lastError, updatedAtUtc: new Date().toISOString() };
      if (lastError === undefined) {
        delete updated.lastError;
      }
      recordings.put(updated);
    }
    await done;
  }

  async list(): Promise<StoredRecording[]> {
    const transaction = (await this.open()).transaction(RECORDINGS, 'readonly');
    return requestResult<StoredRecording[]>(transaction.objectStore(RECORDINGS).getAll());
  }

  async assemble(id: string): Promise<Blob | null> {
    const transaction = (await this.open()).transaction([RECORDINGS, CHUNKS], 'readonly');
    const recording = await requestResult<StoredRecording | undefined>(transaction.objectStore(RECORDINGS).get(id));
    if (!recording) {
      return null;
    }
    const chunkStore = transaction.objectStore(CHUNKS);
    const chunks = await Promise.all(
      Array.from({ length: recording.chunkCount }, (_, index) => requestResult<Blob>(chunkStore.get([id, index]))),
    );
    return new Blob(chunks, { type: recording.mimeType });
  }

  async delete(id: string): Promise<void> {
    const transaction = (await this.open()).transaction([RECORDINGS, CHUNKS], 'readwrite');
    const recordings = transaction.objectStore(RECORDINGS);
    const done = transactionDone(transaction);
    const recording = await requestResult<StoredRecording | undefined>(recordings.get(id));
    for (let index = 0; index < (recording?.chunkCount ?? 0); index++) {
      transaction.objectStore(CHUNKS).delete([id, index]);
    }
    recordings.delete(id);
    await done;
  }

  private open(): Promise<IDBDatabase> {
    this.database ??= new Promise<IDBDatabase>((resolve, reject) => {
      const request = this.factory.open(DATABASE_NAME, DATABASE_VERSION);
      request.onupgradeneeded = () => {
        request.result.createObjectStore(RECORDINGS, { keyPath: 'id' });
        request.result.createObjectStore(CHUNKS);
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => {
        this.database = undefined;
        reject(request.error);
      };
    });
    return this.database;
  }
}
