import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SegmentHistoryComponent } from './segment-history.component';
import { translocoTesting } from '../../i18n/transloco-testing';
import { SegmentVersion, TranscriptSegment } from '../../models/audio-job.model';

const segment: TranscriptSegment = { index: 0, startMs: 0, endMs: 1_000, text: 'Hallo Welt', version: 2 };
const history: SegmentVersion[] = [
  { version: 2, text: 'Hallo Welt', editedBy: 'u1', editedAtUtc: '2026-11-10T10:00:00Z' },
  { version: 1, text: 'Hallo zusammen', editedBy: 'u1', editedAtUtc: '2026-11-10T09:00:00Z' },
  { version: 0, text: 'Hallo zusammen.', editedBy: null, editedAtUtc: null },
];

describe('SegmentHistoryComponent (S32)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SegmentHistoryComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render(canRestore = true) {
    const fixture = TestBed.createComponent(SegmentHistoryComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.componentRef.setInput('segment', segment);
    fixture.componentRef.setInput('canRestore', canRestore);
    const restored: TranscriptSegment[] = [];
    fixture.componentInstance.restored.subscribe((s) => restored.push(s));
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/segments/0/history').flush(history);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, restored };
  }

  it('lists the original and all corrections, newest first, marking the current one', () => {
    const { el } = render();

    const entries = Array.from(el.querySelectorAll('[data-testid="history-entry"]'));
    expect(entries).toHaveLength(3);
    expect(entries[0].textContent).toContain('Current');
    expect(entries[2].textContent).toContain('Original');
  });

  it('highlights what a correction changed', () => {
    const { el } = render();

    const newest = el.querySelectorAll('[data-testid="history-entry"]')[0];
    expect(newest.querySelector('del[data-diff="removed"]')?.textContent).toContain('zusammen');
    expect(newest.querySelector('ins[data-diff="added"]')?.textContent).toContain('Welt');
  });

  it('offers restore for older versions only', () => {
    const { el } = render();

    const entries = Array.from(el.querySelectorAll('[data-testid="history-entry"]'));
    expect(entries[0].querySelector('[data-testid="history-restore"]')).toBeNull();
    expect(entries[1].querySelector('[data-testid="history-restore"]')).not.toBeNull();
    expect(entries[2].querySelector('[data-testid="history-restore"]')).not.toBeNull();
  });

  it('offers no restore to readers', () => {
    const { el } = render(false);

    expect(el.querySelector('[data-testid="history-restore"]')).toBeNull();
  });

  it('restores a version using the current version as guard', () => {
    const { el, fixture, restored } = render();

    (el.querySelectorAll('[data-testid="history-restore"]')[0] as HTMLButtonElement).click();

    const req = http.expectOne('/api/audio-jobs/job-1/segments/0/restore');
    expect(req.request.body).toEqual({ version: 1, expectedVersion: 2 });
    req.flush({ ...segment, text: 'Hallo zusammen', version: 3 });
    fixture.detectChanges();
    expect(restored).toEqual([{ ...segment, text: 'Hallo zusammen', version: 3 }]);
  });

  it('shows a conflict message when the segment changed in the meantime', () => {
    const { el, fixture } = render();

    (el.querySelectorAll('[data-testid="history-restore"]')[0] as HTMLButtonElement).click();
    http.expectOne('/api/audio-jobs/job-1/segments/0/restore').flush({}, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('changed in the meantime');
  });

  it('shows an error when the history cannot be loaded', () => {
    const fixture = TestBed.createComponent(SegmentHistoryComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.componentRef.setInput('segment', segment);
    fixture.detectChanges();

    http.expectOne('/api/audio-jobs/job-1/segments/0/history').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
  });
});
