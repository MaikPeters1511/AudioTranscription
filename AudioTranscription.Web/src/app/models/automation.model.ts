/** Scopes of an API key (S28): reading and changing jobs are granted separately. */
export type ApiKeyScope = 'jobs:read' | 'jobs:write';

export const API_KEY_SCOPES: readonly ApiKeyScope[] = ['jobs:read', 'jobs:write'];

/** An API key as listed: never the key itself. */
export interface ApiKey {
  id: string;
  name: string;
  /** The 8 public characters, to tell keys apart. */
  prefix: string;
  scopes: ApiKeyScope[];
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
}

/** The response of creating a key: the only time `key` is delivered. */
export interface CreatedApiKey extends ApiKey {
  key: string;
}

export type WebhookEvent = 'job.completed' | 'job.failed';

export const WEBHOOK_EVENTS: readonly WebhookEvent[] = ['job.completed', 'job.failed'];

export interface Webhook {
  id: string;
  url: string;
  events: WebhookEvent[];
  isActive: boolean;
  createdAt: string;
}

/** The response of creating a webhook: the only time `secret` is delivered. */
export interface CreatedWebhook extends Webhook {
  secret: string;
}

export type WebhookDeliveryStatus = 'pending' | 'succeeded' | 'failed';

export interface WebhookDelivery {
  id: string;
  event: string;
  jobId: string | null;
  status: WebhookDeliveryStatus;
  attempts: number;
  nextAttemptAt: string;
  lastStatusCode: number | null;
  /** A short fixed error class such as "timeout" or "http 500". */
  lastError: string | null;
  createdAt: string;
  completedAt: string | null;
}
