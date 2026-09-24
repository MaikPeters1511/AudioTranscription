export const SUPPORTED_LANGUAGES = ['de', 'en'] as const;
export type Language = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: Language = 'de';
export const LANGUAGE_STORAGE_KEY = 'lang';

export function isSupportedLanguage(value: string | null | undefined): value is Language {
  return SUPPORTED_LANGUAGES.includes(value as Language);
}

/**
 * Picks the UI language: an explicit, stored choice wins, then the first supported browser
 * language (region ignored, e.g. "de-AT" -> "de"), otherwise the default.
 */
export function detectLanguage(
  stored: string | null,
  browserLanguages: readonly string[],
): Language {
  if (isSupportedLanguage(stored)) {
    return stored;
  }
  const browserMatch = browserLanguages
    .map((l) => l.split('-')[0].toLowerCase())
    .find(isSupportedLanguage);
  return browserMatch ?? DEFAULT_LANGUAGE;
}

/** Reads the stored choice; storage can be unavailable (privacy mode, SSR). */
export function readStoredLanguage(): string | null {
  try {
    return localStorage.getItem(LANGUAGE_STORAGE_KEY);
  } catch {
    return null;
  }
}
