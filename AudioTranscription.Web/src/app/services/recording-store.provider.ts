import { InjectionToken, inject } from '@angular/core';
import { InMemoryRecordingStore } from './in-memory-recording-store';
import { IndexedDbRecordingStore } from './indexeddb-recording-store';
import { RecordingStore } from './recording-store';

/** The browser's IndexedDB factory; replaceable in tests. */
export const IDB_FACTORY = new InjectionToken<IDBFactory | undefined>('IDB_FACTORY', {
  providedIn: 'root',
  factory: () => (typeof indexedDB !== 'undefined' ? indexedDB : undefined),
});

export function createRecordingStore(factory: IDBFactory | undefined): RecordingStore {
  // Without IndexedDB (e.g. some private modes) recordings still work, they just do not survive a reload
  return factory ? new IndexedDbRecordingStore(factory) : new InMemoryRecordingStore();
}

/** The local recording store (S33); tests replace it with an in-memory one. */
export const RECORDING_STORE = new InjectionToken<RecordingStore>('RECORDING_STORE', {
  providedIn: 'root',
  factory: () => createRecordingStore(inject(IDB_FACTORY)),
});
