import { TestBed } from '@angular/core/testing';
import { JobProgressComponent } from './job-progress.component';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('JobProgressComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [JobProgressComponent, translocoTesting('en')] });
  });

  function render(percent: number | undefined, announce = false) {
    const fixture = TestBed.createComponent(JobProgressComponent);
    fixture.componentRef.setInput('percent', percent);
    fixture.componentRef.setInput('announce', announce);
    fixture.detectChanges();
    return fixture;
  }

  it('shows a labelled determinate bar with ARIA values', () => {
    const el: HTMLElement = render(37).nativeElement;

    const bar = el.querySelector('progress')!;
    expect(bar.getAttribute('value')).toBe('37');
    expect(bar.getAttribute('max')).toBe('100');
    expect(bar.getAttribute('aria-valuenow')).toBe('37');
    expect(bar.getAttribute('aria-valuemin')).toBe('0');
    expect(bar.getAttribute('aria-valuemax')).toBe('100');
    expect(bar.getAttribute('aria-label')).toBe('Transcription progress');
    expect(el.textContent).toContain('37 %');
  });

  it('shows an indeterminate bar while no value is known', () => {
    const el: HTMLElement = render(undefined).nativeElement;

    const bar = el.querySelector('progress')!;
    expect(bar.hasAttribute('value')).toBe(false);
    expect(bar.hasAttribute('aria-valuenow')).toBe(false);
    expect(bar.getAttribute('aria-label')).toBe('Transcription progress');
  });

  it('announces progress politely in steps of 10 %', () => {
    const fixture = render(37, true);
    const live = () => fixture.nativeElement.querySelector('[aria-live="polite"]') as HTMLElement;

    expect(live().textContent!.trim()).toBe('Transcription 30 % done');

    fixture.componentRef.setInput('percent', 39);
    fixture.detectChanges();
    expect(live().textContent!.trim()).toBe('Transcription 30 % done');

    fixture.componentRef.setInput('percent', 41);
    fixture.detectChanges();
    expect(live().textContent!.trim()).toBe('Transcription 40 % done');
  });

  it('announces nothing below 10 % or when announcing is off', () => {
    expect((render(5, true).nativeElement.querySelector('[aria-live]') as HTMLElement).textContent!.trim()).toBe('');
    expect(render(55, false).nativeElement.querySelector('[aria-live]')).toBeNull();
  });
});
