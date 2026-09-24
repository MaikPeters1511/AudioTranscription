import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Language, LANGUAGE_STORAGE_KEY, isSupportedLanguage, DEFAULT_LANGUAGE } from './language';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private transloco = inject(TranslocoService);
  private document = inject(DOCUMENT);

  private readonly active = signal<Language>(this.initialLanguage());
  readonly current = this.active.asReadonly();

  constructor() {
    this.document.documentElement.lang = this.active();
  }

  set(language: Language): void {
    this.active.set(language);
    this.transloco.setActiveLang(language);
    this.document.documentElement.lang = language;
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage unavailable (privacy mode): the choice just isn't remembered across reloads.
    }
  }

  toggle(): void {
    this.set(this.active() === 'de' ? 'en' : 'de');
  }

  private initialLanguage(): Language {
    const active = this.transloco.getActiveLang();
    return isSupportedLanguage(active) ? active : DEFAULT_LANGUAGE;
  }
}
