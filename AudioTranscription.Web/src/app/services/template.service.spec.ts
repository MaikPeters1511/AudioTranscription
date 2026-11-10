import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TemplateService } from './template.service';
import { PostprocessingTemplate, SaveTemplateRequest } from '../models/audio-job.model';

const template = (id: string, name: string, isBuiltIn = false): PostprocessingTemplate => ({
  id,
  name,
  description: '',
  prompt: 'p',
  outputSchema: '{"type":"object"}',
  isBuiltIn,
});

const request: SaveTemplateRequest = { name: 'Neu', description: '', prompt: 'p', outputSchema: '{"type":"object"}' };

describe('TemplateService (S26)', () => {
  let service: TemplateService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TemplateService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the templates', () => {
    service.load();
    http.expectOne('/api/templates').flush([template('a', 'Protokoll', true), template('b', 'Eigene')]);

    expect(service.templates().map((t) => t.name)).toEqual(['Protokoll', 'Eigene']);
    expect(service.loadFailed()).toBe(false);
  });

  it('reports a failed load and shows no templates', () => {
    service.load();
    http.expectOne('/api/templates').flush({}, { status: 500, statusText: 'Server Error' });

    expect(service.templates()).toEqual([]);
    expect(service.loadFailed()).toBe(true);
  });

  it('creates, updates and deletes a template and reloads the list each time', () => {
    service.create(request).subscribe();
    const post = http.expectOne({ method: 'POST', url: '/api/templates' });
    expect(post.request.body).toEqual(request);
    post.flush(template('c', 'Neu'));
    http.expectOne({ method: 'GET', url: '/api/templates' }).flush([template('c', 'Neu')]);

    service.update('c', { ...request, name: 'Anders' }).subscribe();
    const put = http.expectOne({ method: 'PUT', url: '/api/templates/c' });
    expect(put.request.body.name).toBe('Anders');
    put.flush(template('c', 'Anders'));
    http.expectOne({ method: 'GET', url: '/api/templates' }).flush([template('c', 'Anders')]);

    service.delete('c').subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/templates/c' }).flush(null);
    http.expectOne({ method: 'GET', url: '/api/templates' }).flush([]);

    expect(service.templates()).toEqual([]);
  });
});
