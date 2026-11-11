import { TestBed } from '@angular/core/testing';
import { PwaInstallService } from './pwa-install.service';

function installEvent(outcome: 'accepted' | 'dismissed' = 'accepted') {
  const event = new Event('beforeinstallprompt', { cancelable: true }) as Event & { prompt: ReturnType<typeof vi.fn>; userChoice: Promise<{ outcome: string }> };
  event.prompt = vi.fn().mockResolvedValue(undefined);
  event.userChoice = Promise.resolve({ outcome });
  return event;
}

describe('PwaInstallService', () => {
  beforeEach(() => {
    // Node's own localStorage is unusable without a file; an in-memory one behaves like the browser's
    const data = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => data.get(key) ?? null,
      setItem: (key: string, value: string) => void data.set(key, value),
      removeItem: (key: string) => void data.delete(key),
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('offers the installation once the browser says the app is installable', () => {
    const service = TestBed.inject(PwaInstallService);
    expect(service.canInstall()).toBe(false);

    const event = installEvent();
    window.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true); // the browser's own mini-infobar is replaced by ours
    expect(service.canInstall()).toBe(true);
  });

  it('shows the browser prompt on install and stops offering afterwards', async () => {
    const service = TestBed.inject(PwaInstallService);
    const event = installEvent();
    window.dispatchEvent(event);

    await service.install();

    expect(event.prompt).toHaveBeenCalled();
    expect(service.canInstall()).toBe(false);
  });

  it('stops offering when the app got installed in another way', () => {
    const service = TestBed.inject(PwaInstallService);
    window.dispatchEvent(installEvent());

    window.dispatchEvent(new Event('appinstalled'));

    expect(service.canInstall()).toBe(false);
  });

  it('remembers that the hint was dismissed', () => {
    const service = TestBed.inject(PwaInstallService);
    window.dispatchEvent(installEvent());

    service.dismiss();
    expect(service.canInstall()).toBe(false);

    TestBed.resetTestingModule();
    const later = TestBed.inject(PwaInstallService);
    window.dispatchEvent(installEvent());
    expect(later.canInstall()).toBe(false);
  });
});
