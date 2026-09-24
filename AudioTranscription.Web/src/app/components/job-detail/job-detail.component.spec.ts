import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { JobDetailComponent } from './job-detail.component';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJob, AudioJobStatus } from '../../models/audio-job.model';

const completedJob = (overrides: Partial<AudioJob>): AudioJob =>
  ({
    id: 'job-1',
    fileName: 'meeting.mp3',
    fileSizeBytes: 1024,
    contentType: 'audio/mpeg',
    status: AudioJobStatus.Completed,
    createdAtUtc: '2026-09-24T10:00:00Z',
    ...overrides,
  }) as AudioJob;

describe('JobDetailComponent', () => {
  let jobService: AudioJobService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [JobDetailComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    jobService = TestBed.inject(AudioJobService);
  });

  function render(job: AudioJob) {
    const fixture = TestBed.createComponent(JobDetailComponent);
    jobService.selectedJob.set(job);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the post-processed transcript when available', () => {
    const fixture = render(
      completedJob({ rawTranscript: 'aehm hallo welt', processedTranscript: 'Hallo Welt.' }),
    );

    expect(fixture.componentInstance.transcript()).toBe('Hallo Welt.');
    expect(fixture.nativeElement.textContent).toContain('Hallo Welt.');
    expect(fixture.nativeElement.textContent).not.toContain('aehm hallo welt');
  });

  it('falls back to the raw transcript without post-processing', () => {
    const fixture = render(completedJob({ rawTranscript: 'hallo welt' }));

    expect(fixture.componentInstance.transcript()).toBe('hallo welt');
    expect(fixture.componentInstance.wordCount()).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('hallo welt');
  });

  it('does not render a transcript section for unfinished jobs', () => {
    const fixture = render(
      completedJob({ status: AudioJobStatus.Processing, rawTranscript: 'teilweise' }),
    );

    expect(fixture.nativeElement.textContent).not.toContain('teilweise');
  });
});
