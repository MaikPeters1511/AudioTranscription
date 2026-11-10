import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChapterNavComponent } from './chapter-nav.component';
import { ChapterService } from '../../services/chapter.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { Chapter, ChapterList } from '../../models/audio-job.model';

const chapters: Chapter[] = [
  { id: 'a', position: 0, startMs: 0, endMs: 60_000, title: 'Begrüßung', summary: 'Alle werden begrüßt.' },
  { id: 'b', position: 1, startMs: 60_000, endMs: 180_000, title: 'Budget', summary: 'Zahlen und Planung.' },
];

describe('ChapterNavComponent (S25)', () => {
  let http: HttpTestingController;
  let service: ChapterService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChapterNavComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ChapterService);
  });

  afterEach(() => http.verify());

  function render(list: ChapterList, canEdit = true, currentMs = 0) {
    const fixture = TestBed.createComponent(ChapterNavComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.componentRef.setInput('currentMs', currentMs);
    const sought: number[] = [];
    fixture.componentInstance.seek.subscribe((ms) => sought.push(ms));
    fixture.detectChanges();
    http.expectOne('/api/audio-jobs/job-1/chapters').flush(list);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, sought };
  }

  const markers = (el: HTMLElement) => el.querySelectorAll<HTMLButtonElement>('[data-testid="chapter-marker"]');
  const rows = (el: HTMLElement) => el.querySelectorAll<HTMLElement>('[data-testid="chapter-row"]');

  it('shows nothing to a reader when there are no chapters', () => {
    const { el } = render({ status: 'None', chapters: [] }, false);

    expect(el.textContent?.trim()).toBe('');
  });

  it('offers the owner to generate chapters when there are none', () => {
    const { el } = render({ status: 'None', chapters: [] });

    expect(el.querySelector('[data-testid="chapter-generate"]')?.textContent).toContain('Generate chapters');
  });

  it('starts the generation and shows that it is running', () => {
    const { fixture, el } = render({ status: 'None', chapters: [] });

    el.querySelector<HTMLButtonElement>('[data-testid="chapter-generate"]')!.click();
    http.expectOne({ method: 'POST', url: '/api/audio-jobs/job-1/chapters' }).flush({ status: 'Pending', chapters: [] });
    fixture.detectChanges();

    expect(el.querySelector('[role="status"]')?.textContent).toContain('being generated');
    expect(el.querySelector('[data-testid="chapter-generate"]')).toBeNull();
  });

  it('lists the chapters with their time and summary', () => {
    const { el } = render({ status: 'Completed', chapters });

    expect(rows(el)).toHaveLength(2);
    expect(rows(el)[1].textContent).toContain('Budget');
    expect(rows(el)[1].textContent).toContain('1:00');
    expect(rows(el)[1].textContent).toContain('Zahlen und Planung.');
  });

  it('draws one marker per chapter on the timeline, positioned by time', () => {
    const { el } = render({ status: 'Completed', chapters });

    expect(markers(el)).toHaveLength(2);
    expect(markers(el)[1].style.left).toBe(`${(60_000 / 180_000) * 100}%`);
    expect(markers(el)[1].getAttribute('aria-label')).toContain('Budget');
  });

  it('jumps to a chapter from the list and from the timeline', () => {
    const { el, sought } = render({ status: 'Completed', chapters });

    rows(el)[1].querySelector<HTMLButtonElement>('[data-testid="chapter-jump"]')!.click();
    markers(el)[0].click();

    expect(sought).toEqual([60_000, 0]);
  });

  it('marks the chapter being played as current', () => {
    const { el } = render({ status: 'Completed', chapters }, true, 90_000);

    expect(rows(el)[1].querySelector('[aria-current="true"]')).not.toBeNull();
    expect(rows(el)[0].querySelector('[aria-current="true"]')).toBeNull();
  });

  it('lets the owner rename a chapter', () => {
    const { fixture, el } = render({ status: 'Completed', chapters });

    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid="chapter-edit"]')!.click();
    fixture.detectChanges();
    const title = el.querySelector<HTMLInputElement>('[data-testid="chapter-title-input"]')!;
    expect(title.value).toBe('Begrüßung');
    title.value = 'Willkommen';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="chapter-save"]')!.click();

    const req = http.expectOne('/api/audio-jobs/job-1/chapters/a');
    expect(req.request.body).toEqual({ title: 'Willkommen', summary: 'Alle werden begrüßt.' });
    req.flush({ ...chapters[0], title: 'Willkommen' });
    fixture.detectChanges();
    expect(rows(el)[0].textContent).toContain('Willkommen');
    expect(el.querySelector('[data-testid="chapter-title-input"]')).toBeNull();
  });

  it('does not save an empty title', () => {
    const { fixture, el } = render({ status: 'Completed', chapters });
    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid="chapter-edit"]')!.click();
    fixture.detectChanges();
    const title = el.querySelector<HTMLInputElement>('[data-testid="chapter-title-input"]')!;

    title.value = '  ';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('[data-testid="chapter-save"]')!.disabled).toBe(true);
  });

  it('asks twice before deleting a chapter', () => {
    const { fixture, el } = render({ status: 'Completed', chapters });

    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid="chapter-delete"]')!.click();
    fixture.detectChanges();
    http.expectNone({ method: 'DELETE', url: '/api/audio-jobs/job-1/chapters/a' });
    el.querySelector<HTMLButtonElement>('[data-testid="chapter-delete-confirm"]')!.click();

    http.expectOne({ method: 'DELETE', url: '/api/audio-jobs/job-1/chapters/a' }).flush(null);
    http.expectOne({ method: 'GET', url: '/api/audio-jobs/job-1/chapters' }).flush({ status: 'Completed', chapters: [chapters[1]] });
    fixture.detectChanges();
    expect(rows(el)).toHaveLength(1);
  });

  it('gives readers no edit, delete or generate controls', () => {
    const { el } = render({ status: 'Completed', chapters }, false);

    expect(el.querySelector('[data-testid="chapter-edit"]')).toBeNull();
    expect(el.querySelector('[data-testid="chapter-delete"]')).toBeNull();
    expect(el.querySelector('[data-testid="chapter-generate"]')).toBeNull();
  });

  it('explains a failed generation without technical details and allows a retry', () => {
    const { el } = render({ status: 'Failed', errorMessage: 'SqlException at line 3', chapters: [] });

    const alert = el.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain('could not be generated');
    expect(alert.textContent).not.toContain('SqlException');
    expect(el.querySelector('[data-testid="chapter-generate"]')).not.toBeNull();
  });

  it('reports a service that is not available', () => {
    const { fixture, el } = render({ status: 'None', chapters: [] });

    el.querySelector<HTMLButtonElement>('[data-testid="chapter-generate"]')!.click();
    http.expectOne({ method: 'POST', url: '/api/audio-jobs/job-1/chapters' }).flush({}, { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('not available');
    expect(service.state().status).toBe('None');
  });
});
