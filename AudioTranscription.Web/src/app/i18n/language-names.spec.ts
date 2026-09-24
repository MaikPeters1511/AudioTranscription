import { languageName } from './language-names';

describe('languageName', () => {
  it('names a language in the UI language', () => {
    expect(languageName('de', 'de')).toBe('Deutsch');
    expect(languageName('de', 'en')).toBe('German');
    expect(languageName('fr', 'de')).toBe('Französisch');
  });

  it('falls back to the code for unknown or invalid codes', () => {
    expect(languageName('zz', 'en')).toBe('zz');
    expect(languageName('not a code', 'en')).toBe('not a code');
  });
});
