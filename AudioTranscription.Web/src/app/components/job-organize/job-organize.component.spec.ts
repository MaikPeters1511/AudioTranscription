import { TestBed } from '@angular/core/testing';
import { JobOrganizeComponent } from './job-organize.component';
import { OrganizeService } from '../../services/organize.service';
import { ToastService } from '../../services/toast.service';
import { AudioJob, AudioJobStatus } from '../../models/audio-job.model';
import { translocoTesting } from '../../i18n/transloco-testing';
import { signal } from '@angular/core';

describe('JobOrganizeComponent (S20)', () => {
  const organize = {
    tags: signal([{ id: 't1', name: 'Budget' }, { id: 't2', name: 'Eilig' }]),
    folderOptions: signal([{ id: 'f1', name: 'Projekte', depth: 0 }, { id: 'f2', name: 'Alpha', depth: 1 }]),
    setFavorite: vi.fn(),
    setJobTags: vi.fn(),
    setJobFolder: vi.fn(),
  };
  const toast = { error: vi.fn(), success: vi.fn() };

  const job = (extra: Partial<AudioJob> = {}) =>
    ({ id: 'job-1', fileName: 'a.mp3', status: AudioJobStatus.Completed, isOwner: true, tags: [], isFavorite: false, folderId: null, ...extra }) as AudioJob;

  function render(j: AudioJob) {
    organize.setFavorite.mockReset().mockResolvedValue(undefined);
    organize.setJobTags.mockReset().mockResolvedValue(undefined);
    organize.setJobFolder.mockReset().mockResolvedValue(undefined);
    toast.error.mockReset();
    TestBed.configureTestingModule({
      imports: [JobOrganizeComponent, translocoTesting('en')],
      providers: [{ provide: OrganizeService, useValue: organize }, { provide: ToastService, useValue: toast }],
    });
    const fixture = TestBed.createComponent(JobOrganizeComponent);
    fixture.componentRef.setInput('job', j);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the favorite state as a pressed toggle button', () => {
    const { el } = render(job({ isFavorite: true }));

    const button = el.querySelector('[data-testid=favorite]') as HTMLButtonElement;
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(button.textContent).toContain('Favorite');
  });

  it('toggles the favorite', () => {
    const { el } = render(job({ isFavorite: false }));

    (el.querySelector('[data-testid=favorite]') as HTMLButtonElement).click();

    expect(organize.setFavorite).toHaveBeenCalledWith('job-1', true);
  });

  it('counts the tags and checks the ones on the job', () => {
    const { el } = render(job({ tags: [{ id: 't2', name: 'Eilig' }] }));

    expect(el.querySelector('[data-testid=tags-summary]')?.textContent).toContain('Tags (1)');
    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('[data-testid=tags-menu] input[type=checkbox]'));
    expect(boxes.map((b) => b.checked)).toEqual([false, true]);
  });

  it('adds and removes a tag by sending the complete new set', () => {
    const { el } = render(job({ tags: [{ id: 't2', name: 'Eilig' }] }));
    const [budget, urgent] = Array.from(el.querySelectorAll<HTMLInputElement>('[data-testid=tags-menu] input[type=checkbox]'));

    budget.checked = true;
    budget.dispatchEvent(new Event('change'));
    expect(organize.setJobTags).toHaveBeenLastCalledWith('job-1', ['t2', 't1']);

    urgent.checked = false;
    urgent.dispatchEvent(new Event('change'));
    expect(organize.setJobTags).toHaveBeenLastCalledWith('job-1', []);
  });

  it('files the job in the chosen folder, and at the top level for the empty choice', () => {
    const { el } = render(job({ folderId: 'f1' }));
    const select = el.querySelector('[data-testid=folder-select]') as HTMLSelectElement;
    expect(Array.from(select.options).map((o) => o.textContent?.replace(/\u00a0/g, ' ').trim())).toEqual(['No folder', 'Projekte', 'Alpha']);
    expect(select.options[1].selected).toBe(true);

    select.value = 'f2';
    select.dispatchEvent(new Event('change'));
    expect(organize.setJobFolder).toHaveBeenLastCalledWith('job-1', 'f2');

    select.value = '';
    select.dispatchEvent(new Event('change'));
    expect(organize.setJobFolder).toHaveBeenLastCalledWith('job-1', null);
  });

  it('gives a reader of a shared job favorite and tags, but no folder', () => {
    const { el } = render(job({ isOwner: false, sharedByEmail: 'alice@example.com' }));

    expect(el.querySelector('[data-testid=favorite]')).not.toBeNull();
    expect(el.querySelector('[data-testid=tags-summary]')).not.toBeNull();
    expect(el.querySelector('[data-testid=folder-select]')).toBeNull();
  });

  it('labels the folder select', () => {
    const { el } = render(job());

    const select = el.querySelector('[data-testid=folder-select]') as HTMLSelectElement;
    expect(el.querySelector(`label[for="${select.id}"]`)?.textContent).toContain('Folder');
  });

  it('shows a clean message when a change fails', async () => {
    const { fixture, el } = render(job());
    organize.setFavorite.mockRejectedValue(new Error('internal details')); // after render: it resets the mocks

    (el.querySelector('[data-testid=favorite]') as HTMLButtonElement).click();
    await vi.waitFor(() => expect(toast.error).toHaveBeenCalledWith('That did not work. Please try again.'));
    expect(fixture.nativeElement.textContent).not.toContain('internal details');
  });
});
