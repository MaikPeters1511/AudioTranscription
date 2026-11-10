import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiKeyService } from './api-key.service';
import { ApiKey } from '../models/automation.model';

const listed = (id: string, name: string): ApiKey => ({
  id,
  name,
  prefix: 'ABCDEFGH',
  scopes: ['jobs:read'],
  createdAt: '2026-11-10T12:00:00Z',
  expiresAt: null,
  lastUsedAt: null,
});

describe('ApiKeyService (S28)', () => {
  let service: ApiKeyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ApiKeyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the keys', async () => {
    const loaded = service.load();
    http.expectOne('/api/api-keys').flush([listed('k1', 'CI')]);
    await loaded;

    expect(service.keys().map((k) => k.name)).toEqual(['CI']);
  });

  it('creates a key, returns the secret once and keeps it out of the list', async () => {
    service.keys.set([listed('k1', 'Old')]);

    const created = service.create('New', ['jobs:read', 'jobs:write'], null);
    const request = http.expectOne('/api/api-keys');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'New', scopes: ['jobs:read', 'jobs:write'], expiresAt: null });
    request.flush({ ...listed('k2', 'New'), key: 'at_ABCDEFGH_secret' });
    const result = await created;

    expect(result.key).toBe('at_ABCDEFGH_secret');
    expect(service.keys().map((k) => k.name)).toEqual(['New', 'Old']);
    expect(JSON.stringify(service.keys())).not.toContain('secret');
  });

  it('deletes a key', async () => {
    service.keys.set([listed('k1', 'A'), listed('k2', 'B')]);

    const deleted = service.delete('k1');
    const request = http.expectOne('/api/api-keys/k1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await deleted;

    expect(service.keys().map((k) => k.id)).toEqual(['k2']);
  });
});
