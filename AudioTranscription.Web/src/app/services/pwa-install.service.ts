import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

/** The browser's install prompt (not part of the standard DOM typings yet). */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

const DISMISSED_KEY = 'installHintDismissed';

function readDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISSED_KEY) === '1';
  } catch {
    return false; // storage blocked: the hint simply comes back next time
  }
}

/** Installation of the app as PWA (S33): keeps the browser's install prompt for our own hint. */
@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  private readonly installEvent = signal<BeforeInstallPromptEvent | null>(null);
  private readonly dismissed = signal(readDismissed());
  readonly canInstall = computed(() => this.installEvent() !== null && !this.dismissed());

  constructor() {
    const onPrompt = (event: Event) => {
      event.preventDefault();
      this.installEvent.set(event as BeforeInstallPromptEvent);
    };
    const onInstalled = () => this.installEvent.set(null);
    window.addEventListener('beforeinstallprompt', onPrompt);
    window.addEventListener('appinstalled', onInstalled);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('beforeinstallprompt', onPrompt);
      window.removeEventListener('appinstalled', onInstalled);
    });
  }

  async install(): Promise<void> {
    const event = this.installEvent();
    if (!event) {
      return;
    }
    await event.prompt();
    await event.userChoice;
    this.installEvent.set(null); // the event can be used only once
  }

  dismiss(): void {
    this.dismissed.set(true);
    try {
      localStorage.setItem(DISMISSED_KEY, '1');
    } catch {
      // storage blocked: the hint comes back next time
    }
  }
}
