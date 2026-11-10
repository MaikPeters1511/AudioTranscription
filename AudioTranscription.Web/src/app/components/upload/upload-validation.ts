/** Client-side checks for an upload; the API enforces the same limits (S14). */
export const MAX_UPLOAD_BYTES = 500_000_000;

const ALLOWED_TYPES = [
  'audio/mpeg', 'audio/wav', 'audio/x-wav',
  'audio/mp4', 'audio/x-m4a', 'audio/ogg', 'audio/webm',
  'video/mp4', 'video/webm', 'video/x-matroska', 'video/quicktime',
];

const ALLOWED_EXTENSIONS = ['mp3', 'wav', 'm4a', 'ogg', 'webm', 'mp4', 'mkv', 'mov'];

/** The i18n key suffix under `upload.` of the first violated rule, or null if the file may be uploaded. */
export function validateUploadFile(file: File): 'tooLarge' | 'invalidType' | null {
  if (file.size > MAX_UPLOAD_BYTES) return 'tooLarge';
  const extension = file.name.toLowerCase().split('.').pop() ?? '';
  if (!ALLOWED_TYPES.includes(file.type) && !ALLOWED_EXTENSIONS.includes(extension)) return 'invalidType';
  return null;
}
