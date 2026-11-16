/**
 * Node 25 and later define their own global `localStorage`/`sessionStorage`. Without `--localstorage-file` they are
 * unusable, and they hide the ones of the test DOM, so every spec that touches the storage fails on a newer Node
 * than the CI uses. This gives the tests a plain in-memory storage whenever the global one does not work.
 */
class MemoryStorage implements Storage {
  private readonly items = new Map<string, string>();

  get length(): number {
    return this.items.size;
  }

  clear(): void {
    this.items.clear();
  }

  getItem(key: string): string | null {
    return this.items.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.items.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.items.delete(key);
  }

  setItem(key: string, value: string): void {
    this.items.set(key, String(value));
  }

  [name: string]: unknown;
}

function usable(name: 'localStorage' | 'sessionStorage'): boolean {
  try {
    return typeof (globalThis as Record<string, unknown>)[name] === 'object' && typeof globalThis[name]?.clear === 'function';
  } catch {
    // Reading the global may itself throw when the storage is not available
    return false;
  }
}

for (const name of ['localStorage', 'sessionStorage'] as const) {
  if (!usable(name)) {
    Object.defineProperty(globalThis, name, { value: new MemoryStorage(), configurable: true, writable: true });
  }
}
