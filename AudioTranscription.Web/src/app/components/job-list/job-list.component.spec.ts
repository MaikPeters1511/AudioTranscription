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
});
