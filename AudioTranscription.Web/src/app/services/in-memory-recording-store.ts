import { RecordingStatus, RecordingStore, StoredRecording } from './recording-store';

/** Keeps recordings in memory only: the fallback without IndexedDB, and the store used in unit tests. */
export class InMemoryRecordingStore extends RecordingStore {
  private readonly recordings = new Map<string, StoredRecording>();
  private readonly chunks = new Map<string, Blob[]>();

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
    this.recordings.set(recording.id, recording);
    this.chunks.set(recording.id, []);
    return { ...recording };
  }

  async appendChunk(id: string, chunk: Blob): Promise<void> {
    const recording = this.recordings.get(id);
    if (!recording) {
      throw new Error(`Recording ${id} does not exist`);
    }
    this.chunks.get(id)!.push(chunk);
    recording.chunkCount += 1;
    recording.sizeBytes += chunk.size;
    recording.updatedAtUtc = new Date().toISOString();
  }

  async setStatus(id: string, status: RecordingStatus, lastError?: string): Promise<void> {
    const recording = this.recordings.get(id);
    if (!recording) {
      return;
    }
    recording.status = status;
    recording.lastError = lastError;
    recording.updatedAtUtc = new Date().toISOString();
    if (lastError === undefined) {
      delete recording.lastError;
    }
  }

  async list(): Promise<StoredRecording[]> {
    return [...this.recordings.values()].map((r) => ({ ...r }));
  }

  async assemble(id: string): Promise<Blob | null> {
    const recording = this.recordings.get(id);
    return recording ? new Blob(this.chunks.get(id) ?? [], { type: recording.mimeType }) : null;
  }

  async delete(id: string): Promise<void> {
    this.recordings.delete(id);
    this.chunks.delete(id);
  }
}
