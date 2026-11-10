import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../i18n/transloco-testing';
import { ConnectivityService } from '../../services/connectivity.service';
import { OfflineUploadQueueService } from '../../services/offline-upload-queue.service';
import { PwaInstallService } from '../../services/pwa-install.service';
import { StoredRecording } from '../../services/recording-store';
import { InstallPromptComponent } from './install-prompt.component';
import { LogoutConfirmDialogComponent } from './logout-confirm-dialog.component';
import { OfflineBannerComponent } from './offline-banner.component';
import { PendingUploadsComponent } from './pending-uploads.component';

function recording(overrides: Partial<StoredRecording> = {}): StoredRecording {
  return {
    id: 'rec-1',
    userEmail: 'alice@example.com',
    mimeType: 'audio/webm',
    createdAtUtc: '2026-11-10T09:30:00.000Z',
    updatedAtUtc: '2026-11-10T09:31:00.000Z',
    status: 'pending',
    sizeBytes: 2048,
    chunkCount: 2,
    ...overrides,
  };
}

describe('OfflineBannerComponent', () => {
  it('is shown as a status message only while offline', () => {
    const online = signal(true);
    TestBed.configureTestingModule({
      imports: [OfflineBannerComponent, translocoTesting('en')],
      providers: [{ provide: ConnectivityService, useValue: { online } }],
    });
    const fixture = TestBed.createComponent(OfflineBannerComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();

    online.set(false);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[role="status"]');
    expect(banner.textContent).toContain('You are offline');
  });
});

describe('PendingUploadsComponent', () => {
  let items: ReturnType<typeof signal<StoredRecording[]>>;
  let progress: ReturnType<typeof signal<ReadonlyMap<string, number>>>;
  let queue: { items: typeof items; progress: typeof progress; retry: ReturnType<typeof vi.fn>; remove: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    items = signal<StoredRecording[]>([]);
    progress = signal<ReadonlyMap<string, number>>(new Map());
    queue = { items, progress, retry: vi.fn().mockResolvedValue(undefined), remove: vi.fn().mockResolvedValue(undefined) };
    TestBed.configureTestingModule({
      imports: [PendingUploadsComponent, translocoTesting('en')],
      providers: [{ provide: OfflineUploadQueueService, useValue: queue }],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(PendingUploadsComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('renders nothing while the queue is empty', () => {
    const { el } = render();

    expect(el.querySelector('section')).toBeNull();
  });

  it('lists waiting recordings in a labelled list with the privacy hint', () => {
    items.set([recording(), recording({ id: 'rec-2', status: 'failed', lastError: 'network' })]);
    const { el } = render();

    const section = el.querySelector('section')!;
    expect(section.getAttribute('aria-labelledby')).toBeTruthy();
    expect(el.querySelector('#' + section.getAttribute('aria-labelledby'))!.textContent).toContain('Waiting for upload');
    expect(el.querySelectorAll('li')).toHaveLength(2);
    expect(el.textContent).toContain('Waiting for a connection');
    expect(el.textContent).toContain('No connection. The upload is retried automatically.');
    expect(el.textContent).toContain('stored unencrypted in this browser');
  });

  it('shows the progress of the recording that is being uploaded', () => {
    items.set([recording({ status: 'uploading' })]);
    progress.set(new Map([['rec-1', 42]]));
    const { el } = render();

    const bar = el.querySelector('progress')!;
    expect(bar.value).toBe(42);
    expect(bar.getAttribute('aria-label')).toContain('Upload progress');
    expect(el.textContent).toContain('Uploading… 42 %');
  });

  it('offers a retry only for failed recordings', async () => {
    items.set([recording(), recording({ id: 'rec-2', status: 'failed', lastError: 'File too large' })]);
    const { el } = render();

    const retryButtons = [...el.querySelectorAll('button')].filter((b) => b.textContent?.includes('Retry'));
    expect(retryButtons).toHaveLength(1);
    expect(el.textContent).toContain('File too large');
    retryButtons[0].click();

    expect(queue.retry).toHaveBeenCalledWith('rec-2');
  });

  it('asks for a second click before a recording is deleted for good', async () => {
    items.set([recording()]);
    const { fixture, el } = render();

    el.querySelector<HTMLButtonElement>('button[data-action="delete"]')!.click();
    fixture.detectChanges();
    expect(queue.remove).not.toHaveBeenCalled();

    el.querySelector<HTMLButtonElement>('button[data-action="confirm-delete"]')!.click();
    expect(queue.remove).toHaveBeenCalledWith('rec-1');
  });

  it('can back out of the deletion', () => {
    items.set([recording()]);
    const { fixture, el } = render();
    el.querySelector<HTMLButtonElement>('button[data-action="delete"]')!.click();
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('button[data-action="cancel-delete"]')!.click();
    fixture.detectChanges();

    expect(queue.remove).not.toHaveBeenCalled();
    expect(el.querySelector('button[data-action="delete"]')).not.toBeNull();
  });

  it('gives every button a name that says which recording it is about and a touch-friendly size', () => {
    items.set([recording({ status: 'failed' })]);
    const { el } = render();

    for (const button of el.querySelectorAll('button')) {
      expect(button.getAttribute('aria-label')).toContain('2026');
      expect(button.className).toContain('min-h-11');
    }
  });
});

describe('InstallPromptComponent', () => {
  let canInstall: ReturnType<typeof signal<boolean>>;
  let install: { canInstall: typeof canInstall; install: ReturnType<typeof vi.fn>; dismiss: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    canInstall = signal(false);
    install = { canInstall, install: vi.fn().mockResolvedValue(undefined), dismiss: vi.fn() };
    TestBed.configureTestingModule({
      imports: [InstallPromptComponent, translocoTesting('en')],
      providers: [{ provide: PwaInstallService, useValue: install }],
    });
  });

  it('appears only when the app can be installed, and installs or dismisses on request', () => {
    const fixture = TestBed.createComponent(InstallPromptComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('section')).toBeNull();

    canInstall.set(true);
    fixture.detectChanges();
    expect(el.querySelector('section')!.getAttribute('aria-label')).toBe('Installation hint');
    expect(el.textContent).toContain('Install app');

    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Install') && !b.textContent.includes('Not now'))!.click();
    expect(install.install).toHaveBeenCalled();
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Not now'))!.click();
    expect(install.dismiss).toHaveBeenCalled();
  });
});

describe('LogoutConfirmDialogComponent', () => {
  function render(count = 2) {
    TestBed.configureTestingModule({ imports: [LogoutConfirmDialogComponent, translocoTesting('en')] });
    const fixture = TestBed.createComponent(LogoutConfirmDialogComponent);
    fixture.componentRef.setInput('count', count);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('is a modal dialog that names the number of unsent recordings', () => {
    const { el } = render(3);

    const dialog = el.querySelector('[role="alertdialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(el.querySelector('#' + dialog.getAttribute('aria-labelledby'))!.textContent).toContain('Recordings not uploaded');
    expect(el.querySelector('#' + dialog.getAttribute('aria-describedby'))!.textContent).toContain('3 recording(s)');
  });

  it('moves the focus to the safe choice', async () => {
    const { fixture, el } = render();
    await fixture.whenStable();

    expect(document.activeElement).toBe(el.querySelector('button[data-action="cancel"]'));
  });

  it('confirms and cancels with the buttons, and cancels with Escape', () => {
    const { fixture, el } = render();
    const confirmed = vi.fn();
    const cancelled = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);

    el.querySelector<HTMLButtonElement>('button[data-action="confirm"]')!.click();
    el.querySelector<HTMLButtonElement>('button[data-action="cancel"]')!.click();
    el.querySelector('[role="alertdialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(confirmed).toHaveBeenCalledTimes(1);
    expect(cancelled).toHaveBeenCalledTimes(2);
  });
});
