import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { ChatService } from '../../services/chat.service';
import { ChatCitation, ChatScope } from '../../models/audio-job.model';
import { PageTitleService } from '../../i18n/page-title.service';
import { formatTimestamp } from '../transcript-player/segment-time';

/**
 * Chat with the user's transcripts (S24): ask a question about everything or one recording, watch the answer arrive and
 * jump from its citations into the player. The page always says which model answers and whether it is an external service.
 */
@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [RouterLink, TranslocoPipe],
  template: `
    <div class="max-w-5xl mx-auto grid gap-6 md:grid-cols-[16rem_1fr]">
      <aside class="space-y-3" [attr.aria-label]="'chat.history' | transloco">
        <button type="button" class="btn btn-sm btn-primary w-full" data-testid="chat-new" (click)="chat.newConversation()">
          {{ 'chat.new' | transloco }}
        </button>
        <ul class="space-y-1">
          @for (c of chat.conversations(); track c.id) {
            <li class="flex items-center gap-1">
              <button
                type="button"
                class="btn btn-ghost btn-sm grow justify-start truncate"
                data-testid="chat-conversation"
                [attr.aria-current]="c.id === chat.conversationId() ? 'true' : null"
                (click)="chat.open(c.id)"
              >
                {{ c.title }}
              </button>
              <button type="button" class="btn btn-ghost btn-xs" data-testid="chat-delete"
                [attr.aria-label]="'chat.delete' | transloco: { title: c.title }" (click)="chat.delete(c.id)">🗑</button>
            </li>
          }
        </ul>
      </aside>

      <section class="space-y-4" [attr.aria-label]="'chat.title' | transloco">
        <h1 class="text-3xl font-bold">{{ 'chat.title' | transloco }}</h1>

        @if (chat.info(); as info) {
          @if (!info.enabled) {
            <div class="alert alert-warning" role="alert" data-testid="chat-unavailable">{{ 'chat.unavailable' | transloco }}</div>
          } @else {
            <p
              role="status"
              data-testid="chat-provider"
              class="badge py-3 {{ info.external ? 'badge-warning' : 'badge-ghost' }}"
            >
              {{ (info.external ? 'chat.provider.external' : 'chat.provider.local') | transloco: { provider: info.provider, model: info.model } }}
            </p>
          }
        }

        @if (jobId()) {
          <div class="flex flex-wrap items-center gap-2" role="group" [attr.aria-label]="'chat.scope.label' | transloco">
            <button type="button" class="btn btn-xs" data-testid="chat-scope-job" [class.btn-primary]="scopeKind() === 'job'"
              [attr.aria-pressed]="scopeKind() === 'job'" (click)="scopeKind.set('job')">{{ 'chat.scope.job' | transloco }}</button>
            <button type="button" class="btn btn-xs" data-testid="chat-scope-all" [class.btn-primary]="scopeKind() === 'all'"
              [attr.aria-pressed]="scopeKind() === 'all'" (click)="scopeKind.set('all')">{{ 'chat.scope.all' | transloco }}</button>
          </div>
        } @else {
          <p class="text-sm text-base-content/70">{{ 'chat.scope.allHint' | transloco }}</p>
        }

        <ol class="space-y-3 min-h-24" role="log" aria-live="polite" [attr.aria-label]="'chat.messages' | transloco" data-testid="chat-messages">
          @for (message of chat.messages(); track $index) {
            <li class="rounded-lg p-3 {{ message.role === 'user' ? 'bg-base-200 ml-8' : 'bg-base-100 border border-base-300 mr-8' }}" data-testid="chat-message">
              <p class="text-xs font-medium text-base-content/70 mb-1">{{ (message.role === 'user' ? 'chat.you' : 'chat.assistant') | transloco }}</p>
              <p class="whitespace-pre-wrap">{{ message.text }}@if (message.role === 'assistant' && chat.streaming() && $last) {<span class="loading loading-dots loading-xs ml-1" aria-hidden="true"></span>}</p>
              @if (message.citations.length > 0) {
                <ul class="flex flex-wrap gap-2 mt-2" [attr.aria-label]="'chat.sources' | transloco">
                  @for (citation of message.citations; track citation.number) {
                    <li>
                      <a class="badge badge-outline hover:badge-primary py-3" data-testid="chat-citation"
                        [routerLink]="['/jobs', citation.jobId]" [queryParams]="{ t: citation.startMs }"
                        [attr.aria-label]="'chat.citation' | transloco: { number: citation.number, time: time(citation) }">
                        [{{ citation.number }}] {{ time(citation) }}
                      </a>
                    </li>
                  }
                </ul>
              }
            </li>
          }
        </ol>

        @if (chat.error(); as error) {
          <p role="alert" class="text-error text-sm" data-testid="chat-error">{{ 'chat.error.' + error | transloco }}</p>
        }

        <form class="space-y-2" (submit)="$event.preventDefault(); send()">
          <label for="chat-question" class="text-sm font-medium">{{ 'chat.question' | transloco }}</label>
          <textarea
            id="chat-question"
            data-testid="chat-input"
            class="textarea textarea-bordered w-full"
            rows="3"
            maxlength="2000"
            aria-describedby="chat-input-hint"
            [value]="draft()"
            [disabled]="!(chat.info()?.enabled ?? false)"
            (input)="draft.set($any($event.target).value)"
            (keydown.enter)="onEnter($event)"
          ></textarea>
          <p id="chat-input-hint" class="text-xs text-base-content/60">{{ 'chat.hint' | transloco }}</p>
          <div class="flex gap-2">
            <button type="submit" class="btn btn-primary btn-sm" data-testid="chat-send" [disabled]="!canSend()">{{ 'chat.send' | transloco }}</button>
            @if (chat.streaming()) {
              <button type="button" class="btn btn-sm" data-testid="chat-stop" (click)="chat.stop()">{{ 'chat.stop' | transloco }}</button>
            }
          </div>
        </form>
      </section>
    </div>
  `,
})
export class ChatComponent implements OnInit {
  readonly chat = inject(ChatService);
  private route = inject(ActivatedRoute);
  private pageTitle = inject(PageTitleService);

  readonly draft = signal('');
  /** The recording the page was opened for (?job=), if any. */
  readonly jobId = signal<string | null>(null);
  readonly scopeKind = signal<'all' | 'job'>('all');

  readonly canSend = computed(() => (this.chat.info()?.enabled ?? false) && !this.chat.streaming() && this.draft().trim().length > 0);

  ngOnInit(): void {
    this.pageTitle.set('chat.pageTitle');
    const job = this.route.snapshot.queryParamMap.get('job');
    if (job) {
      this.jobId.set(job);
      this.scopeKind.set('job');
    }
    this.chat.loadInfo();
    this.chat.loadConversations();
  }

  time(citation: ChatCitation): string {
    return formatTimestamp(citation.startMs);
  }

  /** Enter sends, Shift+Enter makes a new line. */
  onEnter(event: Event): void {
    const keyboard = event as KeyboardEvent;
    if (keyboard.shiftKey) {
      return;
    }
    event.preventDefault();
    this.send();
  }

  send(): void {
    if (!this.canSend()) {
      return;
    }
    const scope: ChatScope = this.scopeKind() === 'job' && this.jobId() ? { kind: 'job', id: this.jobId()! } : { kind: 'all' };
    const question = this.draft();
    this.draft.set('');
    void this.chat.ask(question, scope);
  }
}
