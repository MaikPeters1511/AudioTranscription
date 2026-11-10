import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ChatComponent } from './chat.component';
import { ChatService } from '../../services/chat.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { ChatConversationSummary, ChatInfo, ChatMessage } from '../../models/audio-job.model';

describe('ChatComponent (S24)', () => {
  const info = signal<ChatInfo | null>({ enabled: true, provider: 'Ollama', model: 'llama3.2', external: false });
  const messages = signal<ChatMessage[]>([]);
  const conversations = signal<ChatConversationSummary[]>([]);
  const streaming = signal(false);
  const error = signal<string | null>(null);
  const conversationId = signal<string | null>(null);
  const service = {
    info, messages, conversations, streaming, error, conversationId,
    loadInfo: vi.fn(), loadConversations: vi.fn(), ask: vi.fn(), stop: vi.fn(), open: vi.fn(), delete: vi.fn(), newConversation: vi.fn(),
  };

  function render(job: string | null = null) {
    Object.values(service).forEach((v) => typeof v === 'function' && (v as ReturnType<typeof vi.fn>).mockReset?.());
    service.ask.mockResolvedValue(undefined);
    info.set({ enabled: true, provider: 'Ollama', model: 'llama3.2', external: false });
    messages.set([]);
    conversations.set([]);
    streaming.set(false);
    error.set(null);
    conversationId.set(null);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ChatComponent, translocoTesting('en')],
      providers: [
        provideRouter([]),
        { provide: ChatService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: (k: string) => (k === 'job' ? job : null) } } } },
      ],
    });
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function type(fixture: ReturnType<typeof render>['fixture'], el: HTMLElement, value: string) {
    const input = el.querySelector<HTMLTextAreaElement>('[data-testid=chat-input]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('loads the model info and the conversations', () => {
    render();

    expect(service.loadInfo).toHaveBeenCalled();
    expect(service.loadConversations).toHaveBeenCalled();
  });

  it('says that a local model answers', () => {
    const { el } = render();

    expect(el.querySelector('[data-testid=chat-provider]')?.textContent).toContain('locally');
    expect(el.querySelector('[data-testid=chat-provider]')?.textContent).toContain('llama3.2');
  });

  it('warns clearly when an external provider answers', () => {
    const { fixture, el } = render();
    info.set({ enabled: true, provider: 'OpenAI', model: 'gpt-x', external: true });
    fixture.detectChanges();

    const badge = el.querySelector('[data-testid=chat-provider]')!;
    expect(badge.textContent).toContain('External service');
    expect(badge.textContent).toContain('OpenAI');
    expect(badge.className).toContain('badge-warning');
  });

  it('explains that the chat is not available and disables the input', () => {
    const { fixture, el } = render();
    info.set({ enabled: false, external: false });
    fixture.detectChanges();

    expect(el.querySelector('[data-testid=chat-unavailable]')).not.toBeNull();
    expect(el.querySelector<HTMLTextAreaElement>('[data-testid=chat-input]')!.disabled).toBe(true);
  });

  it('sends a question with the button and clears the input', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Wann ging es ums Geld?');

    el.querySelector<HTMLButtonElement>('[data-testid=chat-send]')!.click();
    fixture.detectChanges();

    expect(service.ask).toHaveBeenCalledWith('Wann ging es ums Geld?', { kind: 'all' });
    expect(el.querySelector<HTMLTextAreaElement>('[data-testid=chat-input]')!.value).toBe('');
  });

  it('sends with Enter but not with Shift+Enter', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Frage?');
    const input = el.querySelector<HTMLTextAreaElement>('[data-testid=chat-input]')!;

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
    expect(service.ask).not.toHaveBeenCalled();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(service.ask).toHaveBeenCalledWith('Frage?', { kind: 'all' });
  });

  it('does not send an empty question or while an answer is streaming', () => {
    const { fixture, el } = render();
    expect(el.querySelector<HTMLButtonElement>('[data-testid=chat-send]')!.disabled).toBe(true);

    type(fixture, el, 'Frage?');
    streaming.set(true);
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('[data-testid=chat-send]')!.disabled).toBe(true);
    expect(el.querySelector('[data-testid=chat-stop]')).not.toBeNull();
  });

  it('stops the answer', () => {
    const { fixture, el } = render();
    streaming.set(true);
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('[data-testid=chat-stop]')!.click();

    expect(service.stop).toHaveBeenCalled();
  });

  it('shows the messages in a polite live region', () => {
    const { fixture, el } = render();
    messages.set([
      { role: 'user', text: 'Frage?', citations: [] },
      { role: 'assistant', text: 'Antwort [1]', citations: [{ number: 1, jobId: 'j1', startMs: 83000, endMs: 90000 }] },
    ]);
    fixture.detectChanges();

    const log = el.querySelector('[data-testid=chat-messages]')!;
    expect(log.getAttribute('role')).toBe('log');
    expect(log.getAttribute('aria-live')).toBe('polite');
    expect(el.querySelectorAll('[data-testid=chat-message]')).toHaveLength(2);
  });

  it('shows a citation chip that jumps to the passage in the player', () => {
    const { fixture, el } = render();
    messages.set([{ role: 'assistant', text: 'Antwort [1]', citations: [{ number: 1, jobId: 'j1', startMs: 83000, endMs: 90000 }] }]);
    fixture.detectChanges();

    const chip = el.querySelector<HTMLAnchorElement>('[data-testid=chat-citation]')!;
    expect(chip.getAttribute('href')).toBe('/jobs/j1?t=83000');
    expect(chip.textContent).toContain('1:23');
    expect(chip.getAttribute('aria-label')).toContain('Source 1');
  });

  it('shows an error without technical details', () => {
    const { fixture, el } = render();
    error.set('unavailable');
    fixture.detectChanges();

    const alert = el.querySelector('[data-testid=chat-error]')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent).toContain('could not be produced');
  });

  it('asks about the recording it was opened for, and lets the user widen the scope', () => {
    const { fixture, el } = render('job-7');
    type(fixture, el, 'Was wurde beschlossen?');
    el.querySelector<HTMLButtonElement>('[data-testid=chat-send]')!.click();
    expect(service.ask).toHaveBeenLastCalledWith('Was wurde beschlossen?', { kind: 'job', id: 'job-7' });

    el.querySelector<HTMLButtonElement>('[data-testid=chat-scope-all]')!.click();
    fixture.detectChanges();
    type(fixture, el, 'Und sonst?');
    el.querySelector<HTMLButtonElement>('[data-testid=chat-send]')!.click();
    expect(service.ask).toHaveBeenLastCalledWith('Und sonst?', { kind: 'all' });
  });

  it('opens, deletes and starts conversations', () => {
    const { fixture, el } = render();
    conversations.set([{ id: 'c1', title: 'Erste Frage', updatedAtUtc: '2026-11-10T10:00:00Z' }]);
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('[data-testid=chat-conversation]')!.click();
    el.querySelector<HTMLButtonElement>('[data-testid=chat-delete]')!.click();
    el.querySelector<HTMLButtonElement>('[data-testid=chat-new]')!.click();

    expect(service.open).toHaveBeenCalledWith('c1');
    expect(service.delete).toHaveBeenCalledWith('c1');
    expect(service.newConversation).toHaveBeenCalled();
  });

  it('labels the input and the conversation controls', () => {
    const { fixture, el } = render();
    conversations.set([{ id: 'c1', title: 'Erste Frage', updatedAtUtc: '2026-11-10T10:00:00Z' }]);
    fixture.detectChanges();

    expect(el.querySelector('label[for=chat-question]')).not.toBeNull();
    expect(el.querySelector('[data-testid=chat-delete]')!.getAttribute('aria-label')).toContain('Erste Frage');
  });
});
