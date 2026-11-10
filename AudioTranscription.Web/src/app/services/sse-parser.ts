/** One Server-Sent Event as sent by the chat endpoint: a name and a JSON payload. */
export interface SseEvent {
  event: string;
  data: unknown;
}

/**
 * Cuts a stream of text chunks into Server-Sent Events (S24). A chunk can end in the middle of an event, so the rest is
 * kept until the next chunk arrives. Events whose data is not valid JSON are dropped.
 */
export class SseParser {
  private buffer = '';

  feed(chunk: string): SseEvent[] {
    this.buffer += chunk.replace(/\r\n/g, '\n');
    const events: SseEvent[] = [];
    let end: number;
    while ((end = this.buffer.indexOf('\n\n')) >= 0) {
      const block = this.buffer.slice(0, end);
      this.buffer = this.buffer.slice(end + 2);
      const event = this.parseBlock(block);
      if (event) {
        events.push(event);
      }
    }
    return events;
  }

  private parseBlock(block: string): SseEvent | null {
    let name = 'message';
    const data: string[] = [];
    for (const line of block.split('\n')) {
      if (line.startsWith('event:')) {
        name = line.slice('event:'.length).trim();
      } else if (line.startsWith('data:')) {
        data.push(line.slice('data:'.length).trim());
      }
    }
    if (data.length === 0) {
      return null;
    }
    try {
      return { event: name, data: JSON.parse(data.join('\n')) };
    } catch {
      return null;
    }
  }
}
