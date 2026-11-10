import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { QueueComponent } from './queue.component';
import { QueueService } from '../../services/queue.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { AudioJobStatus, QueueEntry } from '../../models/audio-job.model';

const entry = (over: Partial<QueueEntry> & Pick<QueueEntry, 'jobId' | 'position'>): QueueEntry => ({
  fileName: `${over.jobId}.mp3`,
  status: over.position === 0 ? AudioJobStatus.Processing : AudioJobStatus.Pending,
  priority: 0,
  estimatedWaitSeconds: null,
  estimatedProcessingSeconds: null,
  ...over,
});

describe('QueueComponent (S34)', () => {
  const entries = signal<QueueEntry[]>([]);
  const totalWaiting = signal(0);
  const service = { entries, totalWaiting, load: vi.fn(), setPriority: vi.fn(), cancel: vi.fn() };
  const toast = { error: vi.fn(), success: vi.fn() };

  async function render(initial: QueueEntry[] = []) {
    entries.set(initial);
    totalWaiting.set(initial.filter((e) => e.position > 0).length);
    service.load.mockReset().mockResolvedValue(undefined);
    service.setPriority.mockReset().mockResolvedValue(undefined);
    service.cancel.mockReset().mockResolvedValue(undefined);
    toast.error.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [QueueComponent, translocoTesting('en')],
      providers: [
        provideRouter([]),
        { provide: QueueService, useValue: service },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(QueueComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('loads the queue on start', async () => {
    await render();

    expect(service.load).toHaveBeenCalled();
  });

  it('shows an empty state when nothing is queued', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('Nothing is queued');
    expect(el.querySelector('table')).toBeNull();
  });

  it('lists the running job and the waiting ones with their position', async () => {
    const { el } = await render([
      entry({ jobId: 'run', position: 0 }),
      entry({ jobId: 'w1', position: 2 }),
    ]);

    const rows = el.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('run.mp3');
    expect(rows[0].textContent).toContain('Running');
    expect(rows[1].textContent).toContain('w1.mp3');
    expect(rows[1].querySelector('[data-testid=queue-position]')?.textContent?.trim()).toBe('2');
  });

  it('links each job to its detail page', async () => {
    const { el } = await render([entry({ jobId: 'w1', position: 1 })]);

    expect(el.querySelector('tbody a')?.getAttribute('href')).toBe('/jobs/w1');
  });

  it('shows the estimated wait or a dash when unknown', async () => {
    const { el } = await render([
      entry({ jobId: 'a', position: 1, estimatedWaitSeconds: 0 }),
      entry({ jobId: 'b', position: 2, estimatedWaitSeconds: 150 }),
      entry({ jobId: 'c', position: 3, estimatedWaitSeconds: null }),
    ]);

    const waits = Array.from(el.querySelectorAll('[data-testid=queue-wait]')).map((c) => c.textContent?.trim());
    expect(waits).toEqual(['< 1 min', '3 min', '–']);
  });

  it('changes the priority of a waiting job', async () => {
    const { fixture, el } = await render([entry({ jobId: 'w1', position: 1 })]);

    const select = el.querySelector('select[data-testid=queue-priority]') as HTMLSelectElement;
    select.value = '10';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(service.setPriority).toHaveBeenCalledWith('w1', 10);
  });

  it('offers an unusual stored priority as a custom option', async () => {
    const { el } = await render([entry({ jobId: 'w1', position: 1, priority: 42 })]);

    const select = el.querySelector('select[data-testid=queue-priority]') as HTMLSelectElement;
    expect(select.value).toBe('42');
    expect(select.textContent).toContain('Custom (42)');
  });

  it('does not allow changing the priority of the running job', async () => {
    const { el } = await render([entry({ jobId: 'run', position: 0 })]);

    expect((el.querySelector('select[data-testid=queue-priority]') as HTMLSelectElement).disabled).toBe(true);
  });

  it('reports a rejected priority change and reloads the queue', async () => {
    const { fixture, el } = await render([entry({ jobId: 'w1', position: 1 })]);
    service.setPriority.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
    service.load.mockClear();

    const select = el.querySelector('select[data-testid=queue-priority]') as HTMLSelectElement;
    select.value = '10';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledWith('The job already started; its priority can no longer be changed.');
    expect(service.load).toHaveBeenCalled();
  });

  it('cancels a job', async () => {
    const { fixture, el } = await render([entry({ jobId: 'w1', position: 1 })]);

    (el.querySelector('button[data-testid=queue-cancel]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(service.cancel).toHaveBeenCalledWith('w1');
  });

  it('names the job in the accessible label of its buttons', async () => {
    const { el } = await render([entry({ jobId: 'w1', position: 1 })]);

    expect(el.querySelector('button[data-testid=queue-cancel]')?.getAttribute('aria-label')).toBe('Cancel w1.mp3');
    expect(el.querySelector('select[data-testid=queue-priority]')?.getAttribute('aria-label')).toBe('Priority of w1.mp3');
  });

  it('refreshes the queue regularly and stops when the page is left', async () => {
    vi.useFakeTimers();
    try {
      const { fixture } = await render([]);
      service.load.mockClear();

      vi.advanceTimersByTime(5000);
      expect(service.load).toHaveBeenCalledTimes(1);

      fixture.destroy();
      vi.advanceTimersByTime(20000);
      expect(service.load).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
