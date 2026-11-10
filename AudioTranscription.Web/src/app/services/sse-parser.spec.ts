import { SseParser } from './sse-parser';

describe('SseParser (S24)', () => {
  it('reads a complete event', () => {
    const events = new SseParser().feed('event: delta\ndata: {"text":"Hallo"}\n\n');

    expect(events).toEqual([{ event: 'delta', data: { text: 'Hallo' } }]);
  });

  it('reads several events from one chunk', () => {
    const events = new SseParser().feed('event: meta\ndata: {"a":1}\n\nevent: delta\ndata: {"text":"x"}\n\n');

    expect(events.map((e) => e.event)).toEqual(['meta', 'delta']);
  });

  it('waits for the rest of an event that is cut in the middle', () => {
    const parser = new SseParser();

    expect(parser.feed('event: delta\ndata: {"te')).toEqual([]);
    expect(parser.feed('xt":"Hall')).toEqual([]);
    expect(parser.feed('o"}\n\nevent: done\n')).toEqual([{ event: 'delta', data: { text: 'Hallo' } }]);
    expect(parser.feed('data: {"text":"fertig"}\n\n')).toEqual([{ event: 'done', data: { text: 'fertig' } }]);
  });

  it('keeps umlauts and line breaks of the text intact', () => {
    const events = new SseParser().feed('event: delta\ndata: {"text":"Größe\\nzwei"}\n\n');

    expect(events[0].data).toEqual({ text: 'Größe\nzwei' });
  });

  it('understands Windows line endings', () => {
    const events = new SseParser().feed('event: done\r\ndata: {"x":1}\r\n\r\n');

    expect(events).toEqual([{ event: 'done', data: { x: 1 } }]);
  });

  it('drops events without data or with data that is not JSON', () => {
    const events = new SseParser().feed(': keep-alive\n\nevent: delta\ndata: kaputt\n\nevent: delta\ndata: {"text":"ok"}\n\n');

    expect(events).toEqual([{ event: 'delta', data: { text: 'ok' } }]);
  });
});
