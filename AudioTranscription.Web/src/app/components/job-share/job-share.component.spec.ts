import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { JobShareComponent } from './job-share.component';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('JobShareComponent (S19)', () => {
  const jobService = { getShares: vi.fn(), addShare: vi.fn(), removeShare: vi.fn() };
  const toast = { success: vi.fn(), error: vi.fn() };
  const bob = { userId: 'bob-id', email: 'bob@example.com' };

  beforeAll(() => {
    // jsdom has no modal dialog support
    HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    });
    HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    });
  });

  beforeEach(() => {
    Object.values(jobService).forEach((fn) => fn.mockReset());
    toast.success.mockReset();
    jobService.getShares.mockResolvedValue([]);
    jobService.addShare.mockResolvedValue(undefined);
    jobService.removeShare.mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [JobShareComponent, translocoTesting('en')],
      providers: [
        { provide: AudioJobService, useValue: jobService },
        { provide: ToastService, useValue: toast },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(JobShareComponent);
    fixture.componentRef.setInput('jobId', 'job-1');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('[data-action=share]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable(); // ngModel registers its control asynchronously
    return { fixture, el };
  }

  function type(el: HTMLElement, value: string) {
    const input = el.querySelector('input[type=email]') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('loads the current shares when opened and labels the dialog and its input', async () => {
    jobService.getShares.mockResolvedValue([bob]);
    const { el } = await render();

    expect(jobService.getShares).toHaveBeenCalledWith('job-1');
    expect(el.querySelector('dialog')?.getAttribute('aria-labelledby')).toBeTruthy();
    expect(el.querySelector('label[for]')?.textContent).toContain("User's email address");
    expect(el.textContent).toContain('bob@example.com');
  });

  it('shows a hint when the job is not shared yet', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('Not shared with anyone yet.');
  });

  it('shares with the typed e-mail address and reloads the list', async () => {
    const { fixture, el } = await render();
    jobService.getShares.mockResolvedValue([bob]);
    type(el, ' bob@example.com ');

    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(jobService.addShare).toHaveBeenCalledWith('job-1', 'bob@example.com');
    expect(toast.success).toHaveBeenCalled();
    expect(el.textContent).toContain('bob@example.com');
  });

  it('does nothing for an empty address', async () => {
    const { fixture, el } = await render();

    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(jobService.addShare).not.toHaveBeenCalled();
  });

  it('explains an unknown user without leaking server details', async () => {
    const { fixture, el } = await render();
    jobService.addShare.mockRejectedValue(new HttpErrorResponse({ status: 400, error: 'internal details' }));
    type(el, 'nobody@example.com');

    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = el.querySelector('[role=alert]');
    expect(alert?.textContent).toContain('no user with this email address');
    expect(el.textContent).not.toContain('internal details');
    expect(el.querySelector('input[type=email]')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('removes a share with a button labelled for that user', async () => {
    jobService.getShares.mockResolvedValue([bob]);
    const { fixture, el } = await render();
    jobService.getShares.mockResolvedValue([]);

    const revoke = el.querySelector('[data-action=revoke]') as HTMLButtonElement;
    expect(revoke.getAttribute('aria-label')).toBe('Remove access for bob@example.com');
    revoke.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(jobService.removeShare).toHaveBeenCalledWith('job-1', 'bob-id');
    expect(el.textContent).not.toContain('bob@example.com');
  });
});
