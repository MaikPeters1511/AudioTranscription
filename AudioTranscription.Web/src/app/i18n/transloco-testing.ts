import { TranslocoTestingModule, TranslocoTestingOptions } from '@jsverse/transloco';
import de from '../../../public/i18n/de.json';
import en from '../../../public/i18n/en.json';
import { Language, SUPPORTED_LANGUAGES } from './language';

/** Transloco with the real translation files, for component tests. */
export function translocoTesting(language: Language = 'de', options: TranslocoTestingOptions = {}) {
  return TranslocoTestingModule.forRoot({
    langs: { de, en },
    translocoConfig: { availableLangs: [...SUPPORTED_LANGUAGES], defaultLang: language },
    preloadLangs: true,
    ...options,
  });
}
