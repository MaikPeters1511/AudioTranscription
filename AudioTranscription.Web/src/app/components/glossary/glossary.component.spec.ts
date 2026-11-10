import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { GlossaryComponent } from './glossary.component';
import { GlossaryService } from '../../services/glossary.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('GlossaryComponent (S22)', () => {
  const glossaries = signal([{ id: 'g1', name: 'Projekt Alpha', terms: ['Kubernetes', 'Müller GmbH'], estimatedTokens: 8, maxTokens: 224 }]);
  const service = {
    glossaries,
    load: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    estimateTokens: (terms: string[]) => Math.ceil(terms.join(', ').length / 3),
  };
  const toast = { error: vi.fn(), success: vi.fn() };

  async function render() {
    service.load.mockReset().mockResolvedValue(undefined);
    service.create.mockReset().mockResolvedValue(undefined);
    service.update.mockReset().mockResolvedValue(undefined);
    service.delete.mockReset().mockResolvedValue(undefined);
    toast.error.mockReset();
    TestBed.configureTestingModule({
      imports: [GlossaryComponent, translocoTesting('en')],
      providers: [{ provide: GlossaryService, useValue: service }, { provide: ToastService, useValue: toast }],
    });
    const fixture = TestBed.createComponent(GlossaryComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function type(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }, el: HTMLElement, selector: string, value: string) {
    const input = el.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('lists the glossaries with their terms', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('Projekt Alpha');
    expect(el.textContent).toContain('Kubernetes');
    expect(el.textContent).toContain('Müller GmbH');
  });

  it('creates a glossary from a name and one term per line', async () => {
    const { fixture, el } = await render();

    await type(fixture, el, '[data-testid=glossary-name]', ' Neu ');
    await type(fixture, el, '[data-testid=glossary-terms]', 'Eins\n\n  Zwei  \n');
    (el.querySelector('[data-testid=glossary-form]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(service.create).toHaveBeenCalledWith('Neu', ['Eins', 'Zwei']);
  });

  it('shows the token usage and blocks saving when the glossary is too long', async () => {
    const { fixture, el } = await render();
    const tooMany = Array.from({ length: 100 }, (_, i) => `Fachbegriff${i}`).join('\n');

    await type(fixture, el, '[data-testid=glossary-name]', 'Lang');
    await type(fixture, el, '[data-testid=glossary-terms]', tooMany);

    expect(el.querySelector('[data-testid=glossary-tokens]')?.textContent).toContain('224');
    expect(el.querySelector('[data-testid=glossary-too-long]')).not.toBeNull();
    expect((el.querySelector('[data-testid=glossary-save]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('fills the form when editing and updates the glossary', async () => {
    const { fixture, el } = await render();

    (el.querySelector('[data-action=edit-glossary]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect((el.querySelector('[data-testid=glossary-name]') as HTMLInputElement).value).toBe('Projekt Alpha');
    expect((el.querySelector('[data-testid=glossary-terms]') as HTMLTextAreaElement).value).toBe('Kubernetes\nMüller GmbH');

    await type(fixture, el, '[data-testid=glossary-terms]', 'Nur einer');
    (el.querySelector('[data-testid=glossary-form]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(service.update).toHaveBeenCalledWith('g1', 'Projekt Alpha', ['Nur einer']);
  });

  it('asks before deleting', async () => {
    const { fixture, el } = await render();

    (el.querySelector('[data-action=delete-glossary]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service.delete).not.toHaveBeenCalled();

    (el.querySelector('[data-action=confirm-delete-glossary]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(service.delete).toHaveBeenCalledWith('g1');
  });

  it('shows a friendly message for a duplicate name, not the server text', async () => {
    const { fixture, el } = await render();
    service.create.mockRejectedValue(new HttpErrorResponse({ status: 409, error: 'internal detail' }));

    await type(fixture, el, '[data-testid=glossary-name]', 'Projekt Alpha');
    await type(fixture, el, '[data-testid=glossary-terms]', 'X');
    (el.querySelector('[data-testid=glossary-form]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    const error = el.querySelector('[data-testid=glossary-error]');
    expect(error?.textContent).toContain('already exists');
    expect(el.textContent).not.toContain('internal detail');
  });
});
