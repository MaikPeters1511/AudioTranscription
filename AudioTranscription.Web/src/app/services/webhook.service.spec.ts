import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WebhookService } from './webhook.service';
import { Webhook, WebhookDelivery } from '../models/automation.model';

const hook = (id: string, url = 'https://hooks.example.com/x'): Webhook => ({
  id,
  url,
  events: ['job.completed'],
  isActive: true,
  createdAt: '2026-11-10T12:00:00Z',
});

const delivery = (id: string): WebhookDelivery => ({
  id,
  event: 'webhook.test',
  jobId: null,
  status: 'succeeded',
  attempts: 1,
  nextAttemptAt: '2026-11-10T12:00:00Z',
  lastStatusCode: 200,
  lastError: null,
  createdAt: '2026-11-10T12:00:00Z',
  completedAt: '2026-11-10T12:00:01Z',
});

describe('WebhookService (S28)', () => {
  let service: WebhookService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(WebhookService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the webhooks', async () => {
    const loaded = service.load();
    http.expectOne('/api/webhooks').flush([hook('w1')]);
    await loaded;

    expect(service.webhooks().map((w) => w.id)).toEqual(['w1']);
  });

  it('creates a webhook, returns the secret once and keeps it out of the list', async () => {
    const created = service.create('https://a.example.com/x', ['job.completed', 'job.failed']);
    const request = http.expectOne('/api/webhooks');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ url: 'https://a.example.com/x', events: ['job.completed', 'job.failed'] });
    request.flush({ ...hook('w2', 'https://a.example.com/x'), secret: 'whsec_abc' });
    const result = await created;

    expect(result.secret).toBe('whsec_abc');
    expect(service.webhooks().map((w) => w.id)).toEqual(['w2']);
    expect(JSON.stringify(service.webhooks())).not.toContain('whsec_');
  });

  it('updates a webhook in the list', async () => {
    service.webhooks.set([hook('w1'), hook('w2')]);

    const updated = service.update('w1', 'https://b.example.com/y', ['job.failed'], false);
    const request = http.expectOne('/api/webhooks/w1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ url: 'https://b.example.com/y', events: ['job.failed'], isActive: false });
    request.flush({ ...hook('w1', 'https://b.example.com/y'), events: ['job.failed'], isActive: false });
    await updated;

    expect(service.webhooks()[0]).toMatchObject({ url: 'https://b.example.com/y', isActive: false });
    expect(service.webhooks()[1].id).toBe('w2');
  });

  it('deletes a webhook', async () => {
    service.webhooks.set([hook('w1'), hook('w2')]);

    const deleted = service.delete('w1');
    http.expectOne('/api/webhooks/w1').flush(null);
    await deleted;

    expect(service.webhooks().map((w) => w.id)).toEqual(['w2']);
  });

  it('sends a test event and returns the delivery', async () => {
    const tested = service.test('w1');
    const request = http.expectOne('/api/webhooks/w1/test');
    expect(request.request.method).toBe('POST');
    request.flush(delivery('d1'));

    expect((await tested).status).toBe('succeeded');
  });

  it('loads the delivery log', async () => {
    const loaded = service.deliveries('w1');
    http.expectOne('/api/webhooks/w1/deliveries').flush([delivery('d1'), delivery('d2')]);

    expect((await loaded).map((d) => d.id)).toEqual(['d1', 'd2']);
  });
});
