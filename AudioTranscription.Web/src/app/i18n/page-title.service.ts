import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';

/** Sets the browser tab title from a translation key and keeps it in sync with language changes. */
@Injectable({ providedIn: 'root' })
export class PageTitleService {
  private transloco = inject(TranslocoService);
  private title = inject(Title);
  private subscription?: Subscription;

  set(key: string, params?: Record<string, unknown>): void {
    this.subscription?.unsubscribe();
    this.subscription = this.transloco
      .selectTranslate(key, params)
      .subscribe((text) => this.title.setTitle(text));
  }
}
