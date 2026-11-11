import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { UnassignedJobsComponent } from './unassigned-jobs.component';
import { AdminJobService } from '../../services/admin-job.service';
import { AdminUserService, AdminUser } from '../../services/admin-user.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { AudioJobListItem, AudioJobStatus } from '../../models/audio-job.model';

const job = (id: string, fileName: string): AudioJobListItem =>
  ({ id, fileName, fileSizeBytes: 2048, status: AudioJobStatus.Completed, createdAtUtc: '2026-09-01T10:00:00Z' }) as AudioJobListItem;

const users: AdminUser[] = [
  { id: 'u1', email: 'anna@example.com', roles: ['Admin'], isActive: true },
  { id: 'u2', email: 'ben@example.com', roles: [], isActive: true },
  { id: 'u3', email: 'gesperrt@example.com', roles: [], isActive: false },
];

/** The component awaits promises of the services; let them all run. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('UnassignedJobsComponent (S19-F3)', () => {
  const jobService = { listUnassigned: vi.fn(), assignOwner: vi.fn() };
  const userService = { list: vi.fn() };
  const toast = { success: vi.fn(), error: vi.fn() };

  async function render(jobs: AudioJobListItem[] = [job('j1', 'alt-1.mp3'), job('j2', 'alt-2.mp3')]) {
    jobService.listUnassigned.mockReset().mockResolvedValue(jobs);
    jobService.assignOwner.mockReset().mockResolvedValue(undefined);
    userService.list.mockReset().mockResolvedValue({ items: users, total: users.length });
    toast.success.mockReset();
    toast.error.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [UnassignedJobsComponent, translocoTesting('en')],
      providers: [
        { provide: AdminJobService, useValue: jobService },
        { provide: AdminUserService, useValue: userService },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(UnassignedJobsComponent);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const rows = (el: HTMLElement) => el.querySelectorAll('[data-testid=unassigned-row]');

  function choose(row: Element, userId: string) {
    const select = row.querySelector<HTMLSelectElement>('select')!;
    select.value = userId;
    select.dispatchEvent(new Event('change'));
  }

  it('lists the jobs without an owner', async () => {
    const { el } = await render();

    expect(rows(el)).toHaveLength(2);
    expect(rows(el)[0].textContent).toContain('alt-1.mp3');
  });

  it('shows an empty state when there are none', async () => {
    const { el } = await render([]);

    expect(rows(el)).toHaveLength(0);
    expect(el.querySelector('[data-testid=unassigned-empty]')).not.toBeNull();
  });

  it('offers the active users only', async () => {
    const { el } = await render();

    const options = Array.from(rows(el)[0].querySelectorAll('option')).map((o) => o.textContent?.trim());
    expect(options).toContain('anna@example.com');
    expect(options).toContain('ben@example.com');
    expect(options).not.toContain('gesperrt@example.com');
  });

  it('cannot assign before a user is chosen', async () => {
    const { el } = await render();

    expect(rows(el)[0].querySelector<HTMLButtonElement>('[data-testid=unassigned-assign]')!.disabled).toBe(true);
  });

  it('assigns the job to the chosen user and removes it from the list', async () => {
    const { fixture, el } = await render();
    choose(rows(el)[0], 'u2');
    fixture.detectChanges();

    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid=unassigned-assign]')!.click();
    await settle();
    fixture.detectChanges();

    expect(jobService.assignOwner).toHaveBeenCalledWith('j1', 'u2');
    expect(rows(el)).toHaveLength(1);
    expect(rows(el)[0].textContent).toContain('alt-2.mp3');
    expect(toast.success).toHaveBeenCalled();
  });

  it('tells the admin when the job is already taken and refreshes the list', async () => {
    const { fixture, el } = await render();
    jobService.assignOwner.mockRejectedValue(new HttpErrorResponse({ status: 404 }));
    jobService.listUnassigned.mockResolvedValue([job('j2', 'alt-2.mp3')]);
    choose(rows(el)[0], 'u2');
    fixture.detectChanges();

    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid=unassigned-assign]')!.click();
    await settle();
    fixture.detectChanges();

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('no longer'));
    expect(rows(el)).toHaveLength(1);
  });

  it('shows a clean message for other errors', async () => {
    const { fixture, el } = await render();
    jobService.assignOwner.mockRejectedValue(new HttpErrorResponse({ status: 500, error: 'SqlException at line 3' }));
    choose(rows(el)[0], 'u2');
    fixture.detectChanges();

    rows(el)[0].querySelector<HTMLButtonElement>('[data-testid=unassigned-assign]')!.click();
    await settle();

    expect(toast.error).toHaveBeenCalledWith('The job could not be assigned. Please try again.');
    expect(rows(el)).toHaveLength(2);
  });

  it('reports a list that cannot be loaded, with a retry', async () => {
    jobService.listUnassigned.mockReset().mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    userService.list.mockReset().mockResolvedValue({ items: users, total: 3 });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [UnassignedJobsComponent, translocoTesting('en')],
      providers: [
        { provide: AdminJobService, useValue: jobService },
        { provide: AdminUserService, useValue: userService },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(UnassignedJobsComponent);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('[role=alert]')).not.toBeNull();
    expect(el.querySelector('[data-testid=unassigned-retry]')).not.toBeNull();
  });

  it('labels the table and the user choice for each job', async () => {
    const { el } = await render();

    expect(el.querySelector('table caption')).not.toBeNull();
    expect(rows(el)[0].querySelector('select')!.getAttribute('aria-label')).toContain('alt-1.mp3');
  });
});
