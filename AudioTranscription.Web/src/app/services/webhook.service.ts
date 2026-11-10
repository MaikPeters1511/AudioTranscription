import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CreatedWebhook, Webhook, WebhookDelivery, WebhookEvent } from '../models/automation.model';

/** The user's webhooks and their delivery log (S28). The signing secret is only part of the response of `create`. */
@Injectable({ providedIn: 'root' })
export class WebhookService {
  private http = inject(HttpClient);

  readonly webhooks = signal<Webhook[]>([]);

  async load(): Promise<void> {
    this.webhooks.set(await firstValueFrom(this.http.get<Webhook[]>('/api/webhooks')));
  }

  /** @returns the new webhook including its secret; show it once and drop it. */
  async create(url: string, events: WebhookEvent[]): Promise<CreatedWebhook> {
    const created = await firstValueFrom(this.http.post<CreatedWebhook>('/api/webhooks', { url, events }));
    const { secret: _secret, ...listed } = created;
    this.webhooks.update((current) => [...current, listed]);
    return created;
  }

  async update(id: string, url: string, events: WebhookEvent[], isActive: boolean): Promise<void> {
    const updated = await firstValueFrom(this.http.put<Webhook>(`/api/webhooks/${id}`, { url, events, isActive }));
    this.webhooks.update((current) => current.map((w) => (w.id === id ? updated : w)));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/webhooks/${id}`));
    this.webhooks.update((current) => current.filter((w) => w.id !== id));
  }

  /** Sends one signed test event; the result is the delivery with its outcome. */
  test(id: string): Promise<WebhookDelivery> {
    return firstValueFrom(this.http.post<WebhookDelivery>(`/api/webhooks/${id}/test`, null));
  }

  deliveries(id: string): Promise<WebhookDelivery[]> {
    return firstValueFrom(this.http.get<WebhookDelivery[]>(`/api/webhooks/${id}/deliveries`));
  }
}
