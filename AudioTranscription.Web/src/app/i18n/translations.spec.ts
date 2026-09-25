import de from '../../../public/i18n/de.json';
import en from '../../../public/i18n/en.json';

type Tree = { [key: string]: string | Tree };

function flatten(tree: Tree, prefix = ''): Record<string, string> {
  return Object.entries(tree).reduce<Record<string, string>>((acc, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return typeof value === 'string'
      ? { ...acc, [path]: value }
      : { ...acc, ...flatten(value, path) };
  }, {});
}

describe('translation files', () => {
  const flatDe = flatten(de);
  const flatEn = flatten(en);

  it('define the same keys in German and English', () => {
    expect(Object.keys(flatEn).sort()).toEqual(Object.keys(flatDe).sort());
  });

  it('contain no empty translations', () => {
    const empty = [...Object.entries(flatDe), ...Object.entries(flatEn)].filter(
      ([, v]) => !v.trim(),
    );
    expect(empty).toEqual([]);
  });

  it('use the same interpolation parameters in both languages', () => {
    const params = (text: string) =>
      (text.match(/{{\s*\w+\s*}}/g) ?? []).map((p) => p.replace(/\s/g, '')).sort();
    for (const key of Object.keys(flatDe)) {
      expect(params(flatEn[key] ?? ''), key).toEqual(params(flatDe[key]));
    }
  });
});
