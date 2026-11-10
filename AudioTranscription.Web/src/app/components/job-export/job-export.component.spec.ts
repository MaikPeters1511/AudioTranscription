import { TestBed } from '@angular/core/testing';
import { JobExportComponent } from './job-export.component';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('JobExportComponent (S21)', () => {
  function render(variantId?: string) {
    TestBed.configureTestingModule({ imports: [JobExportComponent, translocoTesting('en')] });
    const fixture = TestBed.createComponent(JobExportComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    if (variantId) {
      fixture.componentRef.setInput('variantId', variantId);
    }
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const link = (el: HTMLElement) => el.querySelector('[data-testid=export-download]') as HTMLAnchorElement;

  it('links to a DOCX export of the raw transcript with timestamps and speakers by default', () => {
    const { el } = render();

    expect(link(el).getAttribute('href')).toBe('/api/audio-jobs/job-1/export?format=docx&timestamps=true&speakers=true');
    expect(link(el).hasAttribute('download')).toBe(true);
  });

  it('offers docx, md, json and pdf', () => {
    const { el } = render();

    const values = Array.from(el.querySelectorAll('[data-testid=export-format] option')).map((o) => (o as HTMLOptionElement).value);
    expect(values).toEqual(['docx', 'md', 'json', 'pdf']);
  });

  it('updates the link when format and options change', () => {
    const { fixture, el } = render();

    const select = el.querySelector('[data-testid=export-format]') as HTMLSelectElement;
    select.value = 'pdf';
    select.dispatchEvent(new Event('change'));
    (el.querySelector('[data-testid=export-timestamps]') as HTMLInputElement).click();
    (el.querySelector('[data-testid=export-speakers]') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(link(el).getAttribute('href')).toBe('/api/audio-jobs/job-1/export?format=pdf&timestamps=false&speakers=false');
  });

  it('exports a variant without timestamp and speaker options', () => {
    const { el } = render('var-7');

    expect(link(el).getAttribute('href')).toBe('/api/audio-jobs/job-1/export?format=docx&variant=var-7');
    expect(el.querySelector('[data-testid=export-timestamps]')).toBeNull();
    expect(el.querySelector('[data-testid=export-speakers]')).toBeNull();
  });

  it('has an accessible name for the menu and the download link', () => {
    const { el } = render();

    expect(el.querySelector('summary')?.textContent).toContain('Export');
    expect(link(el).getAttribute('aria-label')).toBe('Download export');
  });
});
