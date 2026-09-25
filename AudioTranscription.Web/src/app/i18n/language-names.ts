/**
 * Name of a language (ISO-639-1 code) in the given UI language, e.g. ("de", "en") -> "German".
 * Uses the browser's CLDR data, so every code the server allows gets a name without
 * maintaining translations; falls back to the code itself.
 */
export function languageName(code: string, uiLanguage: string): string {
  try {
    const name = new Intl.DisplayNames([uiLanguage], { type: 'language', fallback: 'none' }).of(code);
    return name ?? code;
  } catch (error) {
    if (error instanceof RangeError) {
      return code; // not a well-formed language code
    }
    throw error;
  }
}
