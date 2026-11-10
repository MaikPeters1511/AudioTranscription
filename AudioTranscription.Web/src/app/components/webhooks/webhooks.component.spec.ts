import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { WebhooksComponent } from './webhooks.component';
import { WebhookService } from '../../services/webhook.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { Webhook, WebhookDelivery } from '../../models/automation.model';

const hook = (over: Partial<Webhook> = {}): Webhook => ({
  id: 'w1',
  url: 'https://hooks.example.com/transcripts',
  events: ['job.completed'],
  isActive: true,
  createdAt: '2026-11-10T12:00:00Z',
  ...over,
});

const delivery = (over: Partial<WebhookDelivery> = {}): WebhookDelivery => ({
  id: 'd1',
  event: 'job.completed',
  jobId: 'j1',
  status: 'succeeded',
  attempts: 1,
  nextAttemptAt: '2026-11-10T12:00:00Z',
  lastStatusCode: 200,
  lastError: null,
  createdAt: '2026-11-10T12:00:00Z',
  completedAt: '2026-11-10T12:00:01Z',
  ...over,
});

describe('WebhooksComponent (S28)', () => {
  const webhooks = signal<Webhook[]>([]);
  const service = { webhooks, load: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), test: vi.fn(), deliveries: vi.fn() };
  const toast = { error: vi.fn(), success: vi.fn() };
  const writeText = vi.fn();

  async function render(initial: Webhook[] = [hook()]) {
    webhooks.set(initial);
    service.load.mockReset().mockResolvedValue(undefined);
    service.create.mockReset().mockResolvedValue({ ...hook({ id: 'w2' }), secret: 'whsec_the-secret' });
    service.update.mockReset().mockResolvedValue(undefined);
    service.delete.mockReset().mockResolvedValue(undefined);
    service.test.mockReset().mockResolvedValue(delivery({ id: 'dt', event: 'webhook.test' }));
    service.deliveries.mockReset().mockResolvedValue([
      delivery({ id: 'd2', status: 'failed', attempts: 6, lastStatusCode: 500, lastError: 'http 500' }),
      delivery({ id: 'd1' }),
    ]);
    toast.error.mockReset();
    toast.success.mockReset();
    writeText.mockReset().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    TestBed.configureTestingModule({
      imports: [WebhooksComponent, translocoTesting('en')],
      providers: [
        { provide: WebhookService, useValue: service },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(WebhooksComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function type(fixture: Parameters<typeof settle>[0], el: HTMLElement, selector: string, value: string) {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  const click = (el: HTMLElement, selector: string) => (el.querySelector(selector) as HTMLElement).click();

  it('lists the webhooks with url and events', async () => {
    const { el } = await render([hook({ events: ['job.completed', 'job.failed'] })]);

    expect(el.textContent).toContain('https://hooks.example.com/transcripts');
    expect(el.textContent).toContain('Transcript finished');
    expect(el.textContent).toContain('Transcript failed');
    expect(el.querySelector('[data-testid="webhook-secret"]')).toBeNull();
  });

  it('shows the empty state', async () => {
    const { el } = await render([]);

    expect(el.textContent).toContain('No webhooks yet.');
  });

  it('needs an address and at least one event before it can create', async () => {
    const { fixture, el } = await render();
    const create = el.querySelector('[data-testid="webhook-create"]') as HTMLButtonElement;
    expect(create.disabled).toBe(true);

    await type(fixture, el, '[data-testid="webhook-url"]', 'https://n8n.example.com/hook');
    expect(create.disabled).toBe(false);

    click(el, '[data-testid="event-job.completed"]');
    await settle(fixture);
    expect(create.disabled).toBe(true);
  });

  it('creates a webhook and shows the signing secret once, with a copy button', async () => {
    const { fixture, el } = await render();
    await type(fixture, el, '[data-testid="webhook-url"]', ' https://n8n.example.com/hook ');
    click(el, '[data-testid="event-job.failed"]');
    await settle(fixture);

    click(el, '[data-testid="webhook-create"]');
    await settle(fixture);

    expect(service.create).toHaveBeenCalledWith('https://n8n.example.com/hook', ['job.completed', 'job.failed']);
    const shown = el.querySelector('[data-testid="webhook-secret"]') as HTMLInputElement;
    expect(shown.value).toBe('whsec_the-secret');
    expect(shown.readOnly).toBe(true);
    expect(shown.getAttribute('aria-label')).toBe('Signing secret');
    expect(el.querySelector('[data-testid="webhook-once"]')?.textContent).toContain('only time');

    click(el, '[data-testid="webhook-copy"]');
    await settle(fixture);
    expect(writeText).toHaveBeenCalledWith('whsec_the-secret');
    expect(toast.success).toHaveBeenCalled();

    click(el, '[data-testid="webhook-dismiss"]');
    await settle(fixture);
    expect(el.textContent).not.toContain('the-secret');
  });

  it('shows a clean message for an address the server refuses', async () => {
    const { fixture, el } = await render();
    service.create.mockRejectedValue(new HttpErrorResponse({ status: 400, error: { errors: { url: ['internal address 10.0.0.1'] } } }));
    await type(fixture, el, '[data-testid="webhook-url"]', 'https://10.0.0.1/x');

    click(el, '[data-testid="webhook-create"]');
    await settle(fixture);

    const error = el.querySelector('[data-testid="webhook-error"]');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toContain('public https');
    expect(el.textContent).not.toContain('10.0.0.1 ');
  });

  it('shows the limit message on 409', async () => {
    const { fixture, el } = await render();
    service.create.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
    await type(fixture, el, '[data-testid="webhook-url"]', 'https://a.example.com/x');

    click(el, '[data-testid="webhook-create"]');
    await settle(fixture);

    expect(el.querySelector('[data-testid="webhook-error"]')?.textContent).toContain('limit');
  });

  it('sends a test event and announces the result in a live region', async () => {
    const { fixture, el } = await render();

    click(el, '[data-action="test-webhook"]');
    await settle(fixture);

    expect(service.test).toHaveBeenCalledWith('w1');
    const result = el.querySelector('[data-testid="webhook-test-result"]') as HTMLElement;
    expect(result.getAttribute('role')).toBe('status');
    expect(result.textContent).toContain('Test delivered');
  });

  it('reports a failing test with the HTTP status', async () => {
    const { fixture, el } = await render();
    service.test.mockResolvedValue(delivery({ status: 'failed', lastStatusCode: 500, lastError: 'http 500' }));

    click(el, '[data-action="test-webhook"]');
    await settle(fixture);

    expect(el.querySelector('[data-testid="webhook-test-result"]')?.textContent).toContain('failed');
    expect(el.querySelector('[data-testid="webhook-test-result"]')?.textContent).toContain('500');
  });

  it('shows the delivery log with status, attempts and the error class', async () => {
    const { fixture, el } = await render();

    click(el, '[data-action="show-deliveries"]');
    await settle(fixture);

    expect(service.deliveries).toHaveBeenCalledWith('w1');
    const rows = el.querySelectorAll('[data-testid="delivery-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Failed');
    expect(rows[0].textContent).toContain('6');
    expect(rows[0].textContent).toContain('http 500');
    expect(rows[1].textContent).toContain('Delivered');
    const toggle = el.querySelector('[data-action="show-deliveries"]') as HTMLElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });

  it('tells when there are no deliveries yet', async () => {
    const { fixture, el } = await render();
    service.deliveries.mockResolvedValue([]);

    click(el, '[data-action="show-deliveries"]');
    await settle(fixture);

    expect(el.textContent).toContain('No deliveries yet.');
  });

  it('switches a webhook off and on', async () => {
    const { fixture, el } = await render();

    click(el, '[data-action="toggle-webhook"]');
    await settle(fixture);

    expect(service.update).toHaveBeenCalledWith('w1', 'https://hooks.example.com/transcripts', ['job.completed'], false);
  });

  it('edits url and events', async () => {
    const { fixture, el } = await render();

    click(el, '[data-action="edit-webhook"]');
    await settle(fixture);
    expect((el.querySelector('[data-testid="webhook-url"]') as HTMLInputElement).value).toBe('https://hooks.example.com/transcripts');
    await type(fixture, el, '[data-testid="webhook-url"]', 'https://new.example.com/x');
    click(el, '[data-testid="webhook-create"]');
    await settle(fixture);

    expect(service.update).toHaveBeenCalledWith('w1', 'https://new.example.com/x', ['job.completed'], true);
    expect(service.create).not.toHaveBeenCalled();
  });

  it('asks before deleting and then deletes', async () => {
    const { fixture, el } = await render();

    click(el, '[data-action="delete-webhook"]');
    await settle(fixture);
    expect(service.delete).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Delete this webhook?');

    click(el, '[data-action="confirm-delete-webhook"]');
    await settle(fixture);

    expect(service.delete).toHaveBeenCalledWith('w1');
  });

  it('gives each action a label that names the webhook', async () => {
    const { el } = await render();
    const label = (selector: string) => (el.querySelector(selector) as HTMLElement).getAttribute('aria-label');

    expect(label('[data-action="test-webhook"]')).toContain('hooks.example.com');
    expect(label('[data-action="delete-webhook"]')).toContain('hooks.example.com');
    expect(label('[data-action="show-deliveries"]')).toContain('hooks.example.com');
  });

  it('describes the signature so receivers can verify it', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('X-Webhook-Signature');
  });
});
