import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { detectLanguage, LANGUAGE_STORAGE_KEY } from './language';
import { LanguageService } from './language.service';
import { translocoTesting } from './transloco-testing';

describe('detectLanguage', () => {
  it('prefers a stored, supported choice', () => {
    expect(detectLanguage('en', ['de-DE'])).toBe('en');
  });

  it('ignores an unsupported stored value and uses the browser language', () => {
    expect(detectLanguage('fr', ['en-US', 'de'])).toBe('en');
  });

  it('matches browser languages without region', () => {
    expect(detectLanguage(null, ['de-AT'])).toBe('de');
  });

  it('uses the first supported browser language', () => {
    expect(detectLanguage(null, ['fr-FR', 'en-GB', 'de'])).toBe('en');
  });

  it('falls back to German', () => {
    expect(detectLanguage(null, ['fr', 'it'])).toBe('de');
    expect(detectLanguage(null, [])).toBe('de');
  });
});

describe('LanguageService', () => {
  let service: LanguageService;
  let transloco: TranslocoService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [translocoTesting('de')] });
    service = TestBed.inject(LanguageService);
    transloco = TestBed.inject(TranslocoService);
  });

  it('switches the active language, persists it and updates the document language', () => {
    service.set('en');

    expect(service.current()).toBe('en');
    expect(transloco.getActiveLang()).toBe('en');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('toggles between German and English', () => {
    service.toggle();
    expect(service.current()).toBe('en');

    service.toggle();
    expect(service.current()).toBe('de');
  });
});
