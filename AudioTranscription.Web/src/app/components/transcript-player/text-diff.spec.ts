import { diffWords } from './text-diff';

describe('diffWords', () => {
  it('returns one unchanged part for identical texts', () => {
    expect(diffWords('Hallo Welt', 'Hallo Welt')).toEqual([{ text: 'Hallo Welt', kind: 'same' }]);
  });

  it('marks a replaced word as removed and added', () => {
    const parts = diffWords('Hallo zusammen', 'Hallo Welt');

    expect(parts.filter((p) => p.kind === 'removed').map((p) => p.text)).toEqual(['zusammen']);
    expect(parts.filter((p) => p.kind === 'added').map((p) => p.text)).toEqual(['Welt']);
  });

  it('marks an inserted word as added only', () => {
    const parts = diffWords('Los geht', 'Los geht es');

    expect(parts.some((p) => p.kind === 'removed')).toBe(false);
    expect(parts.filter((p) => p.kind === 'added').map((p) => p.text.trim())).toEqual(['es']);
  });

  it('can be put back together to either version', () => {
    const from = 'Der Zauberer kam gestern an.';
    const to = 'Der Drachentöter kam an.';

    const parts = diffWords(from, to);

    expect(parts.filter((p) => p.kind !== 'added').map((p) => p.text).join('')).toBe(from);
    expect(parts.filter((p) => p.kind !== 'removed').map((p) => p.text).join('')).toBe(to);
  });

  it('handles empty texts', () => {
    expect(diffWords('', 'neu')).toEqual([{ text: 'neu', kind: 'added' }]);
    expect(diffWords('alt', '')).toEqual([{ text: 'alt', kind: 'removed' }]);
    expect(diffWords('', '')).toEqual([]);
  });
});
