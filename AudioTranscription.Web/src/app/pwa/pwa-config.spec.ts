import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

// The tests run from the project folder (AudioTranscription.Web)
const root = process.cwd();
const ngsw = JSON.parse(readFileSync(join(root, 'ngsw-config.json'), 'utf8'));
const manifest = JSON.parse(readFileSync(join(root, 'public/manifest.webmanifest'), 'utf8'));

interface AssetGroup {
  resources: { files: string[]; urls?: string[] };
}

interface ManifestIcon {
  src: string;
  sizes: string;
  purpose?: string;
}

/**
 * S33 / ADR 0012: the service worker caches the app shell and nothing else. These checks guard the rules that
 * keep authenticated API answers and transcripts out of any cache.
 */
describe('ngsw-config.json', () => {
  it('has no data groups, so no API response is ever cached', () => {
    expect(ngsw['dataGroups']).toBeUndefined();
  });

  it('caches only static files, never anything under /api or /hubs', () => {
    const patterns = ngsw.assetGroups.flatMap((group: AssetGroup) => [...group.resources.files, ...(group.resources.urls ?? [])]);

    expect(patterns.length).toBeGreaterThan(0);
    for (const pattern of patterns) {
      expect(pattern).not.toMatch(/\/(api|hubs|uploads)\b/);
      expect(pattern).not.toBe('/**');
    }
    for (const group of ngsw.assetGroups as AssetGroup[]) {
      expect(group.resources.urls).toBeUndefined(); // external URLs: none
    }
  });

  it('keeps API and hub navigations away from the app shell', () => {
    expect(ngsw.navigationUrls).toContain('!/api/**');
    expect(ngsw.navigationUrls).toContain('!/hubs/**');
  });

  it('includes the translation files, so the app also starts offline in both languages', () => {
    const files = ngsw.assetGroups.flatMap((group: AssetGroup) => group.resources.files);

    expect(files).toContain('/i18n/*.json');
  });
});

describe('web app manifest', () => {
  it('makes the app installable: name, standalone display, start URL and icons', () => {
    expect(manifest.name).toBeTruthy();
    expect(manifest.short_name).toBeTruthy();
    expect(manifest.display).toBe('standalone');
    expect(manifest.start_url).toBe('/upload');
    expect(manifest.scope).toBe('/');
    const sizes = manifest.icons.map((icon: ManifestIcon) => icon.sizes);
    expect(sizes).toContain('192x192');
    expect(sizes).toContain('512x512');
    expect(manifest.icons.some((icon: ManifestIcon) => icon.purpose === 'maskable')).toBe(true);
  });

  it('points at icon files that exist', () => {
    for (const icon of manifest.icons as ManifestIcon[]) {
      expect(existsSync(join(root, 'public', icon.src))).toBe(true);
    }
  });
});
