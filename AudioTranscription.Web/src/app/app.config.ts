import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './auth/auth.interceptor';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTransloco } from '@jsverse/transloco';
import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES, detectLanguage, readStoredLanguage } from './i18n/language';
import { LanguageService } from './i18n/language.service';
import { TranslocoHttpLoader } from './i18n/transloco-http-loader';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: detectLanguage(readStoredLanguage(), typeof navigator !== 'undefined' ? navigator.languages : []),
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    // Instantiate early so <html lang> matches the detected language from the start
    provideAppInitializer(() => {
      inject(LanguageService);
    }),
  ],
};
