import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it.each([
    ['/jobs/42', '/jobs/42'],
    ['/upload?x=1', '/upload?x=1'],
  ])('keeps the internal path %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });

  it.each([
    null,
    undefined,
    '',
    'https://evil.example.org',
    '//evil.example.org',
    '/\\evil.example.org',
    'javascript:alert(1)',
    '/login',
  ])('falls back to the start page for %s', (input) => {
    expect(safeReturnUrl(input)).toBe('/');
  });
});
