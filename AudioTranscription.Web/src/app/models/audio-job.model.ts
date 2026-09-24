export enum AudioJobStatus {
  Pending = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
  Cancelled = 4,
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
  /** Detected language, or the requested one if detection was off. */
  language?: string;
  durationSeconds?: number;
  createdAtUtc: string;
  completedAtUtc?: string;
  /** Whisper model chosen at upload, e.g. "Base". */
  model: string;
  /** Language chosen at upload (ISO-639-1); missing means automatic detection. */
  requestedLanguage?: string;
  /** Latest progress of a running job in percent; only set while processing. */
  progressPercent?: number;
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
  /** Latest progress of a running job in percent; only set while processing. */
  progressPercent?: number;
}

/** SignalR event "JobProgress". */
export interface JobProgressEvent {
  jobId: string;
  percent: number;
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

/** Choices for the upload form (GET /api/transcription-options). */
export interface TranscriptionOptions {
  models: string[];
  defaultModel: string;
  /** ISO-639-1 codes; automatic detection is always available in addition. */
  languages: string[];
}

/** Value of the language select for automatic detection. */
export const AUTO_LANGUAGE = 'auto';

export interface TranscriptionSettings {
  model?: string;
  /** ISO-639-1 code or {@link AUTO_LANGUAGE}. */
  language?: string;
}

/** Timed part of the raw transcript (GET /api/audio-jobs/{id}/segments). */
export interface TranscriptSegment {
  index: number;
  startMs: number;
  endMs: number;
  text: string;
}

export type SubtitleFormat = 'srt' | 'vtt';
