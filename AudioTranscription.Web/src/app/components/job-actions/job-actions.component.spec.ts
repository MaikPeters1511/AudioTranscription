import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { JobActionsComponent, JobActionTarget } from './job-actions.component';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { AudioJobStatus } from '../../models/audio-job.model';
import { translocoTesting } from '../../i18n/transloco-testing';
import { Language } from '../../i18n/language';

describe('JobActionsComponent (S09)', () => {
  const jobService = { cancelJob: vi.fn(), retryJob: vi.fn(), deleteJob: vi.fn() };
  const toast = { success: vi.fn(), error: vi.fn(), show: vi.fn() };

  beforeAll(() => {
    // jsdom has no modal dialog support
    HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    });
    HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    });
  });

  function render(
    status: AudioJobStatus,
    options: { compact?: boolean; language?: Language } = {},
  ) {
    Object.values(jobService).forEach((fn) => fn.mockReset().mockResolvedValue(undefined));
    Object.values(toast).forEach((fn) => fn.mockReset());
    TestBed.configureTestingModule({
      imports: [JobActionsComponent, translocoTesting(options.language ?? 'de')],
      providers: [
        { provide: AudioJobService, useValue: jobService },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(JobActionsComponent);
    fixture.componentRef.setInput('job', {
      id: 'job-1',
      fileName: 'meeting.mp3',
      status,
    } satisfies JobActionTarget);
    fixture.componentRef.setInput('compact', options.compact ?? false);
    fixture.detectChanges();
    return fixture;
  }

  const actionButtons = (fixture: ComponentFixture<JobActionsComponent>) =>
    Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('[data-action]'));
  const button = (fixture: ComponentFixture<JobActionsComponent>, action: string) =>
    fixture.nativeElement.querySelector(`[data-action="${action}"]`) as HTMLButtonElement;

  it.each([
    [AudioJobStatus.Pending, ['cancel', 'delete']],
    [AudioJobStatus.Processing, ['cancel', 'delete']],
    [AudioJobStatus.Failed, ['retry', 'delete']],
    [AudioJobStatus.Cancelled, ['retry', 'delete']],
    [AudioJobStatus.Completed, ['delete']],
  ])('offers the actions allowed for status %s', (status, expected) => {
    const fixture = render(status);

    expect(actionButtons(fixture).map((b) => b.dataset['action'])).toEqual(expected);
  });

  it('cancels a job', async () => {
    const fixture = render(AudioJobStatus.Processing);

    button(fixture, 'cancel').click();
    await fixture.whenStable();

    expect(jobService.cancelJob).toHaveBeenCalledWith('job-1');
    expect(toast.show).toHaveBeenCalledWith('Abbruch angefordert', 'info');
  });

  it('retries a job', async () => {
    const fixture = render(AudioJobStatus.Failed);

    button(fixture, 'retry').click();
    await fixture.whenStable();

    expect(jobService.retryJob).toHaveBeenCalledWith('job-1');
    expect(toast.success).toHaveBeenCalledWith('Job neu gestartet');
  });

  it('explains a retry that failed because the upload is gone', async () => {
    const fixture = render(AudioJobStatus.Failed);
    jobService.retryJob.mockRejectedValue(new HttpErrorResponse({ status: 410 }));

    button(fixture, 'retry').click();
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledWith(
      'Die hochgeladene Datei ist nicht mehr vorhanden. Bitte erneut hochladen.',
    );
  });

  it('reports other failures generically', async () => {
    const fixture = render(AudioJobStatus.Processing);
    jobService.cancelJob.mockRejectedValue(new HttpErrorResponse({ status: 500 }));

    button(fixture, 'cancel').click();
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledWith('Aktion fehlgeschlagen. Bitte erneut versuchen.');
  });

  it('asks for confirmation before deleting', async () => {
    const fixture = render(AudioJobStatus.Completed);

    button(fixture, 'delete').click();
    fixture.detectChanges();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(
      dialog.querySelector(`#${dialog.getAttribute('aria-labelledby')}`)?.textContent,
    ).toContain('Job löschen?');
    expect(
      dialog.querySelector(`#${dialog.getAttribute('aria-describedby')}`)?.textContent,
    ).toContain('meeting.mp3');
    expect(jobService.deleteJob).not.toHaveBeenCalled();

    (dialog.querySelector('[data-confirm="delete"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(jobService.deleteJob).toHaveBeenCalledWith('job-1');
    expect(toast.success).toHaveBeenCalledWith('Job gelöscht');
    expect(dialog.hasAttribute('open')).toBe(false);
  });

  it('keeps the job when the confirmation is declined', async () => {
    const fixture = render(AudioJobStatus.Completed);

    button(fixture, 'delete').click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-confirm="abort"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(jobService.deleteJob).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('dialog').hasAttribute('open')).toBe(false);
  });

  it('labels icon-only buttons with action and file name', () => {
    const fixture = render(AudioJobStatus.Failed, { compact: true, language: 'en' });

    expect(actionButtons(fixture).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Retry: meeting.mp3',
      'Delete: meeting.mp3',
    ]);
  });
});
