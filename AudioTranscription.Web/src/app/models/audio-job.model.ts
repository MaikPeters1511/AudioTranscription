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
  /** Whether speaker diarization (S11) was requested at upload. */
  diarizationRequested: boolean;
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
  /** Whether variant generation (S10) is available, i.e. an LLM (Ollama) is configured. */
  postProcessingEnabled: boolean;
  /** Whether speaker diarization (S11) is available, i.e. the sherpa-onnx models are configured. */
  diarizationEnabled: boolean;
}

/** What an on-demand transcript variant (S10) was generated for. */
export enum PostProcessingMode {
  Cleanup = 0,
  Summary = 1,
  BulletPoints = 2,
  ActionItems = 3,
  /** Requires {@link TranscriptVariant.targetLanguage}. */
  Translate = 4,
}

export enum VariantStatus {
  Pending = 0,
  Completed = 1,
  Failed = 2,
}

/** An on-demand result generated from a job's raw transcript (S10). */
export interface TranscriptVariant {
  id: string;
  mode: PostProcessingMode;
  targetLanguage?: string;
  status: VariantStatus;
  text?: string;
  errorMessage?: string;
  createdAtUtc: string;
  completedAtUtc?: string;
}

/** SignalR event "VariantCompleted". */
export interface VariantCompletedEvent {
  jobId: string;
  variantId: string;
  mode: PostProcessingMode;
  status: VariantStatus;
}

/** Value of the language select for automatic detection. */
export const AUTO_LANGUAGE = 'auto';

export interface TranscriptionSettings {
  model?: string;
  /** ISO-639-1 code or {@link AUTO_LANGUAGE}. */
  language?: string;
  /** Requests speaker diarization (S11); only sent when {@link TranscriptionOptions.diarizationEnabled}. */
  diarize?: boolean;
}

/** Timed part of the raw transcript (GET /api/audio-jobs/{id}/segments). */
export interface TranscriptSegment {
  index: number;
  startMs: number;
  endMs: number;
  text: string;
  /** Set only for a diarized job (S11); undefined otherwise or when no speaker overlapped this segment. */
  speakerIndex?: number;
  /** Resolved display name for {@link speakerIndex} ("Sprecher N" until renamed). */
  speakerName?: string;
}

export type SubtitleFormat = 'srt' | 'vtt';

/** A detected speaker of a diarized job (S11), with its current (possibly renamed) display name. */
export interface JobSpeaker {
  index: number;
  displayName: string;
}

/**
 * One full-text search hit (S13, GET /api/search). {@link highlightStart}/{@link highlightLength}
 * are offsets into {@link snippet}, not HTML, so the caller renders its own &lt;mark&gt;.
 */
export interface SearchHit {
  jobId: string;
  fileName: string;
  snippet: string;
  highlightStart: number;
  highlightLength: number;
  /** Set when the hit came from a timed segment (S06); undefined for a job- or variant-level hit. */
  segmentStartMs?: number;
}
