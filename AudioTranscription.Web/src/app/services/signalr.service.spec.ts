import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import * as signalR from '@microsoft/signalr';
import { SignalRService } from './signalr.service';
import { AudioJobService } from './audio-job.service';

describe('SignalRService', () => {
  const handlers: Record<string, (...args: unknown[]) => void> = {};

  beforeEach(() => {
    const fakeConnection = {
      on: (name: string, handler: (...args: unknown[]) => void) => (handlers[name] = handler),
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
      onclose: () => undefined,
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      state: signalR.HubConnectionState.Disconnected,
    };
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockReturnValue(
      fakeConnection as unknown as signalR.HubConnection,
    );
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => vi.restoreAllMocks());

  it('stores JobProgress events per job', () => {
    TestBed.inject(SignalRService).start('');
    const jobs = TestBed.inject(AudioJobService);

    handlers['JobProgress']({ jobId: 'job-1', percent: 42 });

    expect(jobs.progressOf('job-1')).toBe(42);
    expect(jobs.progressOf('job-2')).toBeUndefined();
  });
});
