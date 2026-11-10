import { Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ConnectivityService } from '../../services/connectivity.service';

/** Tells the user that there is no connection and that recordings are kept on the device (S33). */
@Component({
  selector: 'app-offline-banner',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    @if (!connectivity.online()) {
      <div role="status" class="alert alert-warning rounded-none justify-center text-sm" data-testid="offline-banner">
        <span>{{ 'offline.banner' | transloco }}</span>
      </div>
    }
  `,
})
export class OfflineBannerComponent {
  protected connectivity = inject(ConnectivityService);
}
