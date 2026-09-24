import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
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
  function render(language: Language) {
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
      .flush({ items: [job], totalCount: 1, page: 1, pageSize: 20 });
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
});
