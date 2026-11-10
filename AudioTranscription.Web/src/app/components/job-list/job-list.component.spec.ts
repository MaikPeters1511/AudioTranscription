import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Title } from '@angular/platform-browser';
import { JobListComponent } from './job-list.component';
import { AudioJobListItem, AudioJobStatus } from '../../models/audio-job.model';
import { Language } from '../../i18n/language';
import { translocoTesting } from '../../i18n/transloco-testing';

const job: AudioJobListItem = {
  id: 'job-1',
  fileName: 'meeting.mp3',
  fileSizeBytes: 2048,
  status: AudioJobStatus.Completed,
  createdAtUtc: '2026-09-24T10:00:00Z',
} as AudioJobListItem;

describe('JobListComponent', () => {
  function render(language: Language, items: AudioJobListItem[] = [job]) {
    TestBed.configureTestingModule({
      imports: [JobListComponent, translocoTesting(language)],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    const fixture = TestBed.createComponent(JobListComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne((r) => r.url === '/api/audio-jobs')
      .flush({ items, totalCount: items.length, page: 1, pageSize: 20 });
    fixture.detectChanges();
    return fixture;
  }

  it('renders German texts', () => {
    const text = render('de').nativeElement.textContent;

    expect(text).toContain('Transkriptionen');
    expect(text).toContain('Dateiname');
    expect(text).toContain('Abgeschlossen');
  });

  it('renders English texts', () => {
    const fixture = render('en');
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Transcriptions');
    expect(text).toContain('File name');
    expect(text).toContain('Completed');
    expect(text).toContain('1 of 1 entries');
    expect(text).not.toContain('Dateiname');
    expect(TestBed.inject(Title).getTitle()).toBe('Transcriptions · Transcription');
  });

  it('labels the icon-only refresh button', () => {
    const button: HTMLButtonElement =
      render('en').nativeElement.querySelector('button[aria-label]');

    expect(button.getAttribute('aria-label')).toBe('Refresh');
  });

  it('offers compact job actions in each table row without opening the job', () => {
    // jsdom has no <dialog> API; the delete button opens the confirmation dialog
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    const fixture = render('en');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl');

    const deleteButton: HTMLButtonElement = fixture.nativeElement.querySelector('table app-job-actions [data-action="delete"]');
    expect(deleteButton.getAttribute('aria-label')).toBe('Delete: meeting.mp3');

    deleteButton.click();

    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows the progress of running jobs without announcing it', () => {
    const running = { ...job, id: 'job-2', status: AudioJobStatus.Processing, progressPercent: 25 };
    const fixture = render('en', [running, job]);

    const bars = fixture.nativeElement.querySelectorAll('table app-job-progress progress');
    expect(bars.length).toBe(1);
    expect(bars[0].getAttribute('value')).toBe('25');
    expect(fixture.nativeElement.querySelector('[aria-live]')).toBeNull();
  });

  it('marks a job shared by someone else and shows no actions for it (S19)', () => {
    const shared = { ...job, id: 'shared', fileName: 'fremd.mp3', isOwner: false, sharedByEmail: 'alice@example.com' };
    const fixture = render('de', [{ ...job, isOwner: true }, shared]);

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Geteilt von alice@example.com');
    expect(text?.match(/Geteilt von/g)).toHaveLength(2); // table row and mobile card of the shared job only
    expect(fixture.nativeElement.querySelector('[data-action=delete][aria-label*="fremd.mp3"]')).toBeNull();
  });

  describe('organizing (S20)', () => {
    const tagged = {
      id: 'a', fileName: 'tagged.mp3', fileSizeBytes: 1, status: AudioJobStatus.Completed, createdAtUtc: '2026-01-01T00:00:00Z',
      tags: [{ id: 't1', name: 'Budget', color: '#ff8800' }], isFavorite: true,
    } as AudioJobListItem;

    it('shows the tags of a job and its favorite star as a toggle button with the file name in its label', () => {
      const fixture = render('en', [tagged, { ...job, id: 'b', fileName: 'plain.mp3', isFavorite: false }]);
      const el = fixture.nativeElement as HTMLElement;

      expect(el.querySelector('[data-testid=tag]')?.textContent).toContain('Budget');
      const stars = Array.from(el.querySelectorAll<HTMLButtonElement>('[data-testid=star]'));
      expect(stars.map((s) => s.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
      expect(stars[0].getAttribute('aria-label')).toBe('Remove favorite from tagged.mp3');
      expect(stars[1].getAttribute('aria-label')).toBe('Mark plain.mp3 as favorite');
    });

    it('filters by favorites, tag and folder together and can reset them', () => {
      const fixture = render('en', [tagged]);
      const el = fixture.nativeElement as HTMLElement;
      const http = TestBed.inject(HttpTestingController);

      (el.querySelector('[data-testid=filter-favorites]') as HTMLButtonElement).click();
      fixture.detectChanges();
      let req = http.expectOne((r) => r.url === '/api/audio-jobs' && r.params.get('favorite') === 'true');
      expect(req.request.params.has('tag')).toBe(false);
      req.flush({ items: [tagged], totalCount: 1, page: 1, pageSize: 20 });
      fixture.detectChanges();
      expect((el.querySelector('[data-testid=filter-favorites]') as HTMLButtonElement).getAttribute('aria-pressed')).toBe('true');

      const tag = el.querySelector('[data-testid=filter-tag]') as HTMLSelectElement;
      tag.innerHTML = '<option value="">All</option><option value="t1">Budget</option>';
      tag.value = 't1';
      tag.dispatchEvent(new Event('change'));
      req = http.expectOne((r) => r.url === '/api/audio-jobs' && r.params.get('tag') === 't1');
      expect(req.request.params.get('favorite')).toBe('true');
      req.flush({ items: [tagged], totalCount: 1, page: 1, pageSize: 20 });
      fixture.detectChanges();

      (el.querySelector('[data-testid=filter-reset]') as HTMLButtonElement).click();
      req = http.expectOne((r) => r.url === '/api/audio-jobs');
      expect(req.request.params.has('tag') || req.request.params.has('favorite')).toBe(false);
      req.flush({ items: [tagged], totalCount: 1, page: 1, pageSize: 20 });
    });

    it('says so when no job matches the filters, instead of offering the first upload', () => {
      const fixture = render('en', []);
      const el = fixture.nativeElement as HTMLElement;
      const http = TestBed.inject(HttpTestingController);

      (el.querySelector('[data-testid=filter-favorites]') as HTMLButtonElement).click();
      http.expectOne((r) => r.url === '/api/audio-jobs' && r.params.get('favorite') === 'true')
        .flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      fixture.detectChanges();

      expect(el.textContent).toContain('No jobs match these filters.');
      expect(el.textContent).not.toContain('Upload your first');
    });

    it('labels the filter controls', () => {
      const el = render('en').nativeElement as HTMLElement;

      expect(el.querySelector('label[for=filter-tag]')).not.toBeNull();
      expect(el.querySelector('label[for=filter-folder]')).not.toBeNull();
      expect(el.querySelector('[role=group]')?.getAttribute('aria-label')).toBe('Filter jobs');
    });
  });
});
