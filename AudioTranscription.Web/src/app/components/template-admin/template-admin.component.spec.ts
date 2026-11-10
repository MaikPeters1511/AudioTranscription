import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { TemplateAdminComponent } from './template-admin.component';
import { TemplateService } from '../../services/template.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { PostprocessingTemplate } from '../../models/audio-job.model';

const builtIn: PostprocessingTemplate = { id: 'b1', name: 'Besprechungsprotokoll', description: 'Protokoll', prompt: 'p', outputSchema: '{}', isBuiltIn: true };
const custom: PostprocessingTemplate = { id: 'c1', name: 'Eigene', description: '', prompt: 'Fasse zusammen', outputSchema: '{"type":"object"}', isBuiltIn: false };

describe('TemplateAdminComponent (S26)', () => {
  const templates = signal<PostprocessingTemplate[]>([builtIn, custom]);
  const service = { templates, loadFailed: signal(false), load: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() };
  const toast = { error: vi.fn(), success: vi.fn() };

  function render() {
    service.load.mockReset();
    service.create.mockReset().mockReturnValue(of(custom));
    service.update.mockReset().mockReturnValue(of(custom));
    service.delete.mockReset().mockReturnValue(of(undefined));
    toast.error.mockReset();
    toast.success.mockReset();
    TestBed.configureTestingModule({
      imports: [TemplateAdminComponent, translocoTesting('en')],
      providers: [{ provide: TemplateService, useValue: service }, { provide: ToastService, useValue: toast }],
    });
    const fixture = TestBed.createComponent(TemplateAdminComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function fill(fixture: ReturnType<typeof render>['fixture'], values: { name?: string; prompt?: string; schema?: string }) {
    // ngModel registers its controls asynchronously after the first render
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const set = (selector: string, value?: string) => {
      if (value === undefined) return;
      const input = el.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('[data-testid=template-name]', values.name);
    set('[data-testid=template-prompt]', values.prompt);
    set('[data-testid=template-schema]', values.schema);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('loads and lists the templates, marking the built-in ones', () => {
    const { el } = render();

    expect(service.load).toHaveBeenCalled();
    const rows = el.querySelectorAll('[data-testid=template-row]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Built-in');
  });

  it('gives built-in templates no edit or delete controls', () => {
    const { el } = render();

    const rows = el.querySelectorAll('[data-testid=template-row]');
    expect(rows[0].querySelector('[data-testid=template-edit]')).toBeNull();
    expect(rows[0].querySelector('[data-testid=template-delete]')).toBeNull();
    expect(rows[1].querySelector('[data-testid=template-edit]')).not.toBeNull();
  });

  it('creates a template', async () => {
    const { fixture, el } = render();
    await fill(fixture, { name: 'Neu', prompt: 'Fasse zusammen', schema: '{"type":"object"}' });

    el.querySelector<HTMLButtonElement>('[data-testid=template-save]')!.click();

    expect(service.create).toHaveBeenCalledWith({ name: 'Neu', description: '', prompt: 'Fasse zusammen', outputSchema: '{"type":"object"}' });
    expect(toast.success).toHaveBeenCalled();
  });

  it('edits a custom template with its values', async () => {
    const { fixture, el } = render();

    el.querySelectorAll('[data-testid=template-row]')[1].querySelector<HTMLButtonElement>('[data-testid=template-edit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector<HTMLInputElement>('[data-testid=template-name]')!.value).toBe('Eigene');
    el.querySelector<HTMLButtonElement>('[data-testid=template-save]')!.click();
    expect(service.update).toHaveBeenCalledWith('c1', expect.objectContaining({ name: 'Eigene', prompt: 'Fasse zusammen' }));
  });

  it('shows the validation messages of the server at the fields', async () => {
    const { fixture, el } = render();
    service.create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { prompt: ["The placeholder '{{PASSWORD}}' is not known."], outputSchema: ['The output schema is not valid JSON.'] } } })),
    );
    await fill(fixture, { name: 'Neu', prompt: 'Hallo {{PASSWORD}}', schema: 'kaputt' });

    el.querySelector<HTMLButtonElement>('[data-testid=template-save]')!.click();
    fixture.detectChanges();

    const alerts = Array.from(el.querySelectorAll('[role=alert]')).map((a) => a.textContent);
    expect(alerts.some((a) => a?.includes('PASSWORD'))).toBe(true);
    expect(alerts.some((a) => a?.includes('not valid JSON'))).toBe(true);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('shows a clean message for other errors', async () => {
    const { fixture, el } = render();
    service.create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, error: 'SqlException at line 3' })));
    await fill(fixture, { name: 'Neu', prompt: 'x', schema: '{"type":"object"}' });

    el.querySelector<HTMLButtonElement>('[data-testid=template-save]')!.click();

    expect(toast.error).toHaveBeenCalledWith('The template could not be saved.');
  });

  it('asks twice before deleting', () => {
    const { fixture, el } = render();
    const row = el.querySelectorAll('[data-testid=template-row]')[1];

    row.querySelector<HTMLButtonElement>('[data-testid=template-delete]')!.click();
    fixture.detectChanges();
    expect(service.delete).not.toHaveBeenCalled();
    row.querySelector<HTMLButtonElement>('[data-testid=template-delete-confirm]')!.click();

    expect(service.delete).toHaveBeenCalledWith('c1');
  });
});
