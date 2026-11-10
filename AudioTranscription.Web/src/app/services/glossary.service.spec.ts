import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GlossaryService } from './glossary.service';

const glossary = (id: string, name: string, terms: string[] = ['A']) => ({ id, name, terms, estimatedTokens: 1, maxTokens: 224 });

describe('GlossaryService (S22)', () => {
  let service: GlossaryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(GlossaryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the glossaries', async () => {
    const loaded = service.load();
    http.expectOne('/api/glossaries').flush([glossary('g1', 'Alpha')]);
    await loaded;

    expect(service.glossaries().map((g) => g.name)).toEqual(['Alpha']);
  });

  it('creates a glossary and keeps the list sorted by name', async () => {
    service.glossaries.set([glossary('g1', 'Zeta')]);

    const created = service.create('Alpha', ['x', 'y']);
    const request = http.expectOne('/api/glossaries');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Alpha', terms: ['x', 'y'] });
    request.flush(glossary('g2', 'Alpha', ['x', 'y']));
    await created;

    expect(service.glossaries().map((g) => g.name)).toEqual(['Alpha', 'Zeta']);
  });

  it('replaces a glossary on update', async () => {
    service.glossaries.set([glossary('g1', 'Alpha', ['old'])]);

    const updated = service.update('g1', 'Alpha 2', ['new']);
    const request = http.expectOne('/api/glossaries/g1');
    expect(request.request.method).toBe('PUT');
    request.flush(glossary('g1', 'Alpha 2', ['new']));
    await updated;

    expect(service.glossaries()).toEqual([glossary('g1', 'Alpha 2', ['new'])]);
  });

  it('removes a glossary on delete', async () => {
    service.glossaries.set([glossary('g1', 'Alpha'), glossary('g2', 'Beta')]);

    const deleted = service.delete('g1');
    http.expectOne('/api/glossaries/g1').flush(null, { status: 204, statusText: 'No Content' });
    await deleted;

    expect(service.glossaries().map((g) => g.id)).toEqual(['g2']);
  });

  it('estimates tokens like the server: three characters per token over the comma-joined terms', () => {
    expect(service.estimateTokens([])).toBe(0);
    expect(service.estimateTokens(['abc'])).toBe(1);
    expect(service.estimateTokens(['ab', 'cd'])).toBe(2); // "ab, cd" = 6 characters
  });
});
