export enum AudioJobStatus {
  Pending = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
}

export interface AudioJob {
  id: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
  status: AudioJobStatus;
  /** Unmodified Whisper output. */
  rawTranscript?: string;
  /** LLM post-processed transcript; only set when post-processing changed the text. */
  processedTranscript?: string;
  errorMessage?: string;
  language?: string;
  durationSeconds?: number;
  createdAtUtc: string;
  completedAtUtc?: string;
}

export interface AudioJobListItem {
  id: string;
  fileName: string;
  fileSizeBytes: number;
  status: AudioJobStatus;
  language?: string;
  durationSeconds?: number;
  createdAtUtc: string;
  completedAtUtc?: string;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CreateAudioJobResponse {
  id: string;
}
