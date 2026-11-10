import { Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PwaInstallService } from '../../services/pwa-install.service';

/** Hint to install the app (S33); shown only when the browser offers the installation. */
@Component({
  selector: 'app-install-prompt',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    @if (pwa.canInstall()) {
      <section class="alert alert-info mb-4 flex-col items-start sm:flex-row sm:items-center" [attr.aria-label]="'offline.install.region' | transloco">
        <div class="flex-1">
          <h2 class="font-semibold">{{ 'offline.install.title' | transloco }}</h2>
          <p class="text-sm">{{ 'offline.install.text' | transloco }}</p>
        </div>
        <div class="flex gap-2">
          <button type="button" class="btn btn-primary min-h-11" (click)="pwa.install()">{{ 'offline.install.install' | transloco }}</button>
          <button type="button" class="btn btn-ghost min-h-11" (click)="pwa.dismiss()">{{ 'offline.install.dismiss' | transloco }}</button>
        </div>
      </section>
    }
  `,
})
export class InstallPromptComponent {
  protected pwa = inject(PwaInstallService);
}
