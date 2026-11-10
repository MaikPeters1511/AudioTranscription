import { HttpClient } from '@angular/common/http';
import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ChatCitation, ChatConversationSummary, ChatInfo, ChatMessage, ChatScope } from '../models/audio-job.model';
import { SseParser } from './sse-parser';

/** The fetch used for the streamed answer; replaced in unit tests. */
export const CHAT_FETCH = new InjectionToken<typeof fetch>('CHAT_FETCH', {
  providedIn: 'root',
  factory: () => (input, init) => fetch(input, init),
});

export type ChatError = 'unavailable' | 'rateLimit' | 'notFound' | 'failed';

interface ConversationDto {
  id: string;
  title: string;
  messages?: { id: string; role: 'user' | 'assistant'; text: string; citations: ChatCitation[] }[];
}

/**
 * The chat with the user's transcripts (S24): the conversation on screen, the streamed answer as it arrives and the
 * state around it (streaming, error). Answers come as Server-Sent Events over a POST, which only fetch can stream.
 */
@Injectable({ providedIn: 'root' })
export class ChatService {
  private http = inject(HttpClient);
  private fetchFn = inject(CHAT_FETCH);
  private auth = inject(AuthService);
  private router = inject(Router);

  readonly info = signal<ChatInfo | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly conversations = signal<ChatConversationSummary[]>([]);
  readonly conversationId = signal<string | null>(null);
  readonly streaming = signal(false);
  readonly error = signal<ChatError | null>(null);

  private abort?: AbortController;

  loadInfo(): void {
    this.http.get<ChatInfo>('/api/chat/info').subscribe({
      next: (info) => this.info.set(info),
      error: () => this.info.set({ enabled: false, external: false }),
    });
  }

  loadConversations(): void {
    this.http.get<ChatConversationSummary[]>('/api/chat').subscribe({
      next: (list) => this.conversations.set(list),
      error: () => this.conversations.set([]),
    });
  }

  async open(id: string): Promise<void> {
    this.stop();
    this.error.set(null);
    try {
      const conversation = await firstValueFrom(this.http.get<ConversationDto>(`/api/chat/${id}`));
      this.conversationId.set(conversation.id);
      this.messages.set((conversation.messages ?? []).map((m) => ({ id: m.id, role: m.role, text: m.text, citations: m.citations ?? [] })));
    } catch {
      this.error.set('notFound');
    }
  }

  newConversation(): void {
    this.stop();
    this.conversationId.set(null);
    this.messages.set([]);
    this.error.set(null);
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/chat/${id}`));
    if (this.conversationId() === id) {
      this.newConversation();
    }
    this.loadConversations();
  }

  /** Stops the answer that is being streamed; what has arrived stays on screen. */
  stop(): void {
    this.abort?.abort();
    this.abort = undefined;
    this.streaming.set(false);
  }

  async ask(question: string, scope: ChatScope): Promise<void> {
    const text = question.trim();
    if (!text || this.streaming()) {
      return;
    }
    this.error.set(null);
    this.messages.update((m) => [...m, { role: 'user', text, citations: [] }, { role: 'assistant', text: '', citations: [] }]);
    this.streaming.set(true);
    const controller = new AbortController();
    this.abort = controller;

    try {
      const response = await this.fetchFn('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
        body: JSON.stringify({ question: text, scope, conversationId: this.conversationId() }),
        signal: controller.signal,
        credentials: 'same-origin',
      });
      if (!response.ok || !response.body) {
        this.fail(this.errorFor(response.status));
        return;
      }
      await this.read(response.body);
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) {
        this.fail('failed');
      }
    } finally {
      if (this.abort === controller) {
        this.abort = undefined;
      }
      this.streaming.set(false);
      this.dropEmptyAnswer();
      this.loadConversations();
    }
  }

  private async read(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    const parser = new SseParser();
    for (;;) {
      const { done, value } = await reader.read();
      if (done) {
        return;
      }
      for (const event of parser.feed(decoder.decode(value, { stream: true }))) {
        this.apply(event.event, event.data as Record<string, unknown>);
      }
    }
  }

  private apply(event: string, data: Record<string, unknown>): void {
    switch (event) {
      case 'meta':
        this.conversationId.set(data['conversationId'] as string);
        break;
      case 'delta':
        this.updateAnswer((m) => ({ ...m, text: m.text + (data['text'] as string) }));
        break;
      case 'done':
        this.updateAnswer((m) => ({
          ...m,
          id: data['messageId'] as string,
          text: data['text'] as string,
          citations: (data['citations'] as ChatCitation[]) ?? [],
        }));
        break;
      case 'error':
        // The server's message is fixed and free of technical details, but the page shows its own translated text
        this.error.set('unavailable');
        break;
    }
  }

  private updateAnswer(change: (message: ChatMessage) => ChatMessage): void {
    this.messages.update((list) => {
      const last = list.length - 1;
      return last >= 0 && list[last].role === 'assistant' ? [...list.slice(0, last), change(list[last])] : list;
    });
  }

  private dropEmptyAnswer(): void {
    this.messages.update((list) => (list.at(-1)?.role === 'assistant' && list.at(-1)?.text === '' ? list.slice(0, -1) : list));
  }

  private fail(error: ChatError): void {
    this.error.set(error);
  }

  private errorFor(status: number): ChatError {
    if (status === 401) {
      this.auth.markLoggedOut();
      void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return 'failed';
    }
    if (status === 429) {
      return 'rateLimit';
    }
    if (status === 503) {
      return 'unavailable';
    }
    return status === 404 ? 'notFound' : 'failed';
  }
}
