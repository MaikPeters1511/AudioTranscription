import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OrganizeComponent } from './organize.component';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('OrganizeComponent (S20)', () => {
  let http: HttpTestingController;
  const toast = { error: vi.fn(), success: vi.fn() };

  beforeEach(() => {
    toast.error.mockReset();
    TestBed.configureTestingModule({
      imports: [OrganizeComponent, translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  /** Lets the pending promises of the service run, then renders; ngModel registers its controls on the next tick. */
  async function settle(fixture: ComponentFixture<OrganizeComponent>) {
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
  }

  async function render(tags: unknown[] = [], folders: unknown[] = []) {
    const fixture = TestBed.createComponent(OrganizeComponent);
    fixture.detectChanges();
    http.expectOne('/api/tags').flush(tags);
    http.expectOne('/api/folders').flush(folders);
    await settle(fixture);
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const type = (el: HTMLElement, selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  it('labels every field and shows empty states', async () => {
    const { el } = await render();

    for (const id of ['new-tag-name', 'new-tag-color', 'new-folder-name', 'new-folder-parent']) {
      expect(el.querySelector(`label[for=${id}]`), id).not.toBeNull();
    }
    expect(el.textContent).toContain('No tags yet.');
    expect(el.textContent).toContain('No folders yet.');
  });

  it('creates a tag with name and color', async () => {
    const { fixture, el } = await render();
    type(el, '#new-tag-name', ' Budget ');
    type(el, '#new-tag-color', '#ff8800');

    (el.querySelector('#new-tag-name')!.closest('form')!.querySelector('button[type=submit]') as HTMLButtonElement).click();
    const req = http.expectOne('/api/tags');
    expect(req.request.body).toEqual({ name: 'Budget', color: '#ff8800' });
    req.flush({ id: 't1', name: 'Budget', color: '#ff8800' });
    await settle(fixture);

    expect(el.textContent).toContain('Budget');
  });

  it('explains a duplicate tag name without showing the server text', async () => {
    const { fixture, el } = await render();
    type(el, '#new-tag-name', 'Budget');

    (el.querySelector('#new-tag-name')!.closest('form')!.querySelector('button[type=submit]') as HTMLButtonElement).click();
    http.expectOne('/api/tags').flush('SQL internals', { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('already exists');
    expect(el.textContent).not.toContain('SQL internals');
  });

  it('asks before deleting a tag, and a click on cancel keeps it', async () => {
    const { fixture, el } = await render([{ id: 't1', name: 'Budget' }]);

    (el.querySelector('[data-action=delete-tag]') as HTMLButtonElement).click();
    await settle(fixture);
    expect(el.textContent).toContain('Remove the tag from all jobs?');
    http.expectNone('/api/tags/t1');

    (el.querySelector('[data-action=confirm-delete-tag]') as HTMLButtonElement).click();
    http.expectOne('/api/tags/t1').flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(el.textContent).not.toContain('Budget');
  });

  it('shows the folders as an indented tree and creates one inside another', async () => {
    const { fixture, el } = await render([], [{ id: 'f1', name: 'Projekte' }, { id: 'f2', name: 'Alpha', parentId: 'f1' }]);
    expect(el.textContent?.replace(/\u00a0/g, ' ')).toMatch(/Projekte[\s\S]*\s\sAlpha/);

    type(el, '#new-folder-name', 'Beta');
    const parent = el.querySelector('#new-folder-parent') as HTMLSelectElement;
    parent.value = parent.options[1].value; // Projekte
    parent.dispatchEvent(new Event('change'));
    (el.querySelector('#new-folder-name')!.closest('form')!.querySelector('button[type=submit]') as HTMLButtonElement).click();

    const req = http.expectOne('/api/folders');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Beta', parentId: 'f1' });
    req.flush({ id: 'f3', name: 'Beta', parentId: 'f1' });
    await settle(fixture);
  });

  it('renames and moves a folder through an edit form', async () => {
    const { fixture, el } = await render([], [{ id: 'f1', name: 'Projekte' }, { id: 'f2', name: 'Alpha' }]);

    (el.querySelectorAll('[data-action=edit-folder]')[1] as HTMLButtonElement).click(); // Projekte (sorted: Alpha, Projekte)
    await settle(fixture);
    type(el, 'input[name=editName]', 'Vorhaben');
    const parent = el.querySelector('select[name=editParent]') as HTMLSelectElement;
    // the folder itself is not offered as its own parent
    expect(Array.from(parent.options).map((o) => o.textContent?.trim())).toEqual(['Top level', 'Alpha']);
    parent.value = parent.options[1].value;
    parent.dispatchEvent(new Event('change'));
    (el.querySelector('input[name=editName]')!.closest('form')!.querySelector('button[type=submit]') as HTMLButtonElement).click();

    const req = http.expectOne('/api/folders/f1');
    expect(req.request.body).toEqual({ name: 'Vorhaben', parentId: 'f2' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
  });

  it('explains an invalid folder move in plain words', async () => {
    const { fixture, el } = await render([], [{ id: 'f1', name: 'Projekte' }]);
    (el.querySelector('[data-action=edit-folder]') as HTMLButtonElement).click();
    await settle(fixture);

    (el.querySelector('input[name=editName]')!.closest('form')!.querySelector('button[type=submit]') as HTMLButtonElement).click();
    http.expectOne('/api/folders/f1').flush({ detail: 'cycle details' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('not moved into itself');
    expect(el.textContent).not.toContain('cycle details');
  });

  it('tells the user when loading fails', async () => {
    const fixture = TestBed.createComponent(OrganizeComponent);
    fixture.detectChanges();
    http.expectOne('/api/tags').flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/folders').flush([]);
    await settle(fixture);

    expect(fixture.nativeElement.textContent).toContain('could not be loaded');
  });
});
