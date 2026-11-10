import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CHAT_FETCH, ChatService } from './chat.service';

/** A fetch answer whose body arrives in the given pieces. */
function streamResponse(parts: string[], status = 200): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      parts.forEach((p) => controller.enqueue(encoder.encode(p)));
      controller.close();
    },
  });
  return new Response(status === 200 ? body : null, { status, headers: { 'Content-Type': 'text/event-stream' } });
}

const sse = (event: string, data: unknown) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;

describe('ChatService streaming state (S24)', () => {
  let service: ChatService;
  let http: HttpTestingController;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: CHAT_FETCH, useValue: fetchMock }],
    });
    service = TestBed.inject(ChatService);
    http = TestBed.inject(HttpTestingController);
  });

  /** The conversation list is reloaded after every question. */
  function answerListRequest() {
    http.match('/api/chat').forEach((r) => r.flush([]));
  }

  it('shows the question at once and the answer as it arrives', async () => {
    fetchMock.mockResolvedValue(
      streamResponse([
        sse('meta', { conversationId: 'c1' }),
        sse('delta', { text: 'Es ging ' }),
        sse('delta', { text: 'ums Geld.' }),
        sse('done', { messageId: 'm1', text: 'Es ging ums Geld [1].', citations: [{ number: 1, jobId: 'j1', startMs: 60000, endMs: 90000 }] }),
      ]),
    );

    await service.ask('Wann ging es ums Geld?', { kind: 'all' });
    answerListRequest();

    expect(service.conversationId()).toBe('c1');
    expect(service.messages()).toEqual([
      { role: 'user', text: 'Wann ging es ums Geld?', citations: [] },
      { id: 'm1', role: 'assistant', text: 'Es ging ums Geld [1].', citations: [{ number: 1, jobId: 'j1', startMs: 60000, endMs: 90000 }] },
    ]);
    expect(service.streaming()).toBe(false);
    expect(service.error()).toBeNull();
  });

  it('is streaming while the answer comes in and appends the pieces', async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const encoder = new TextEncoder();
    fetchMock.mockResolvedValue(
      new Response(
        new ReadableStream<Uint8Array>({
          async start(controller) {
            controller.enqueue(encoder.encode(sse('delta', { text: 'Anfang ' })));
            await gate;
            controller.enqueue(encoder.encode(sse('delta', { text: 'Ende' })));
            controller.close();
          },
        }),
        { status: 200 },
      ),
    );

    const asking = service.ask('Frage?', { kind: 'all' });
    await vi.waitFor(() => expect(service.messages().at(-1)?.text).toBe('Anfang '));
    expect(service.streaming()).toBe(true);

    release();
    await asking;
    answerListRequest();
    expect(service.messages().at(-1)?.text).toBe('Anfang Ende');
  });

  it('sends the scope and continues the conversation', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('meta', { conversationId: 'c1' }), sse('done', { messageId: 'm', text: 'ok', citations: [] })]));
    await service.ask('Erste?', { kind: 'job', id: 'j9' });
    fetchMock.mockResolvedValue(streamResponse([sse('done', { messageId: 'm2', text: 'ok', citations: [] })]));
    await service.ask('Zweite?', { kind: 'job', id: 'j9' });
    answerListRequest();

    const second = JSON.parse(fetchMock.mock.calls[1][1].body);
    expect(second).toEqual({ question: 'Zweite?', scope: { kind: 'job', id: 'j9' }, conversationId: 'c1' });
  });

  it('ignores a question while an answer is still coming and an empty question', async () => {
    await service.ask('   ', { kind: 'all' });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows an error event without technical details and removes the empty answer', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('meta', { conversationId: 'c1' }), sse('error', { message: 'intern: ECONNREFUSED' })]));

    await service.ask('Frage?', { kind: 'all' });
    answerListRequest();

    expect(service.error()).toBe('unavailable');
    expect(service.messages().map((m) => m.role)).toEqual(['user']);
  });

  it('keeps the part of an answer that arrived before an error', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('delta', { text: 'Halber Satz' }), sse('error', { message: 'x' })]));

    await service.ask('Frage?', { kind: 'all' });
    answerListRequest();

    expect(service.messages().at(-1)?.text).toBe('Halber Satz');
    expect(service.error()).toBe('unavailable');
  });

  it.each([
    [429, 'rateLimit'],
    [503, 'unavailable'],
    [404, 'notFound'],
    [500, 'failed'],
  ])('maps the HTTP status %i to the error %s', async (status, expected) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));

    await service.ask('Frage?', { kind: 'all' });
    answerListRequest();

    expect(service.error()).toBe(expected);
    expect(service.streaming()).toBe(false);
    expect(service.messages().map((m) => m.role)).toEqual(['user']);
  });

  it('shows a failed connection as a plain error', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await service.ask('Frage?', { kind: 'all' });
    answerListRequest();

    expect(service.error()).toBe('failed');
  });

  it('stops the answer on request, keeps what arrived and shows no error', async () => {
    fetchMock.mockImplementation((_url: string, init: RequestInit) =>
      Promise.resolve(
        new Response(
          new ReadableStream<Uint8Array>({
            start(controller) {
              controller.enqueue(new TextEncoder().encode(sse('delta', { text: 'Teil' })));
              init.signal!.addEventListener('abort', () => controller.error(new DOMException('abort', 'AbortError')));
            },
          }),
          { status: 200 },
        ),
      ),
    );

    const asking = service.ask('Frage?', { kind: 'all' });
    await vi.waitFor(() => expect(service.messages().at(-1)?.text).toBe('Teil'));
    service.stop();
    await asking;
    answerListRequest();

    expect(service.streaming()).toBe(false);
    expect(service.error()).toBeNull();
    expect(service.messages().at(-1)?.text).toBe('Teil');
  });

  it('opens a conversation and shows its messages with citations', async () => {
    const opening = service.open('c7');
    http.expectOne('/api/chat/c7').flush({
      id: 'c7',
      title: 'Titel',
      messages: [
        { id: 'a', role: 'user', text: 'Frage', citations: [] },
        { id: 'b', role: 'assistant', text: 'Antwort [1]', citations: [{ number: 1, jobId: 'j', startMs: 5, endMs: 9 }] },
      ],
    });
    await opening;

    expect(service.conversationId()).toBe('c7');
    expect(service.messages().map((m) => m.role)).toEqual(['user', 'assistant']);
    expect(service.messages()[1].citations).toHaveLength(1);
  });

  it('reports a conversation that cannot be opened', async () => {
    const opening = service.open('weg');
    http.expectOne('/api/chat/weg').flush({}, { status: 404, statusText: 'Not Found' });
    await opening;

    expect(service.error()).toBe('notFound');
  });

  it('starts a new conversation with an empty screen', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('meta', { conversationId: 'c1' }), sse('done', { messageId: 'm', text: 'ok', citations: [] })]));
    await service.ask('Frage?', { kind: 'all' });
    answerListRequest();

    service.newConversation();

    expect(service.conversationId()).toBeNull();
    expect(service.messages()).toEqual([]);
  });

  it('deletes a conversation and reloads the list', async () => {
    const deleting = service.delete('c1');
    http.expectOne({ method: 'DELETE', url: '/api/chat/c1' }).flush(null);
    await deleting;

    http.expectOne({ method: 'GET', url: '/api/chat' }).flush([]);
  });

  it('loads which model answers', () => {
    service.loadInfo();
    http.expectOne('/api/chat/info').flush({ enabled: true, provider: 'Ollama', model: 'llama3.2', external: false });

    expect(service.info()).toEqual({ enabled: true, provider: 'Ollama', model: 'llama3.2', external: false });
  });

  it('treats an unreachable info endpoint as no chat', () => {
    service.loadInfo();
    http.expectOne('/api/chat/info').flush({}, { status: 500, statusText: 'Server Error' });

    expect(service.info()?.enabled).toBe(false);
  });
});
