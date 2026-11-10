/**
 * Lifecycle of a locally kept recording (S33, ADR 0012):
 * recording -> draft (stopped) -> pending (queued for upload) -> uploading -> deleted; failed on error.
 */
export type RecordingStatus = 'recording' | 'draft' | 'pending' | 'uploading' | 'failed';

export interface StoredRecording {
  id: string;
  /** Owner (e-mail of the signed-in user); a recording is only shown to and uploaded for its owner. */
  userEmail: string;
  mimeType: string;
  createdAtUtc: string;
  /** Last write (new chunk or status change); used to tell a crashed recording from a running one. */
  updatedAtUtc: string;
  status: RecordingStatus;
  sizeBytes: number;
  chunkCount: number;
  lastError?: string;
}

/** Where recordings are kept locally; a section at a time, so a crash loses at most the last section. */
export abstract class RecordingStore {
  abstract create(userEmail: string, mimeType: string): Promise<StoredRecording>;
  /** Appends the next section. Rejects (e.g. QuotaExceededError) when it cannot be stored. */
  abstract appendChunk(id: string, chunk: Blob): Promise<void>;
  abstract setStatus(id: string, status: RecordingStatus, lastError?: string): Promise<void>;
  abstract list(): Promise<StoredRecording[]>;
  /** All sections joined to one blob, or null when the recording does not exist (any more). */
  abstract assemble(id: string): Promise<Blob | null>;
  abstract delete(id: string): Promise<void>;
}

/** True when the error means that the storage quota is used up. */
export function isQuotaError(error: unknown): boolean {
  return error instanceof DOMException && (error.name === 'QuotaExceededError' || error.code === 22);
}
