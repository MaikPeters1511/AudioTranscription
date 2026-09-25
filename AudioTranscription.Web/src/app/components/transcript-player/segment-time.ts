import { TranscriptSegment } from '../../models/audio-job.model';

/**
 * Index of the segment playing at the given time, or -1 in a pause between segments.
 * Binary search over segments sorted by start time.
 */
export function findActiveSegment(segments: readonly TranscriptSegment[], ms: number): number {
  let low = 0;
  let high = segments.length - 1;
  let candidate = -1;
  while (low <= high) {
    const mid = (low + high) >> 1;
    if (segments[mid].startMs <= ms) {
      candidate = mid;
      low = mid + 1;
    } else {
      high = mid - 1;
    }
  }
  return candidate >= 0 && ms < segments[candidate].endMs ? candidate : -1;
}

/** "m:ss", or "h:mm:ss" from one hour on. */
export function formatTimestamp(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = String(totalSeconds % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`;
}
