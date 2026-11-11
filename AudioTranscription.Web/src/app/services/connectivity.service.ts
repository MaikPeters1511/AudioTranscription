import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/** Whether the browser has a network connection (S33): drives the offline banner and the upload queue. */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private readonly state = signal(typeof navigator === 'undefined' || navigator.onLine !== false);
  readonly online = this.state.asReadonly();

  constructor() {
    const goOnline = () => this.state.set(true);
    const goOffline = () => this.state.set(false);
    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
    });
  }
}
