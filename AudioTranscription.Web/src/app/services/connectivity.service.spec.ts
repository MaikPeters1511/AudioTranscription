import { TestBed } from '@angular/core/testing';
import { ConnectivityService } from './connectivity.service';

function setNavigatorOnline(value: boolean) {
  Object.defineProperty(globalThis.navigator, 'onLine', { value, configurable: true });
}

describe('ConnectivityService', () => {
  afterEach(() => setNavigatorOnline(true));

  it('starts with the state the browser reports', () => {
    setNavigatorOnline(false);

    expect(TestBed.inject(ConnectivityService).online()).toBe(false);
  });

  it('follows the offline and online events', () => {
    setNavigatorOnline(true);
    const service = TestBed.inject(ConnectivityService);
    expect(service.online()).toBe(true);

    window.dispatchEvent(new Event('offline'));
    expect(service.online()).toBe(false);

    window.dispatchEvent(new Event('online'));
    expect(service.online()).toBe(true);
  });

  it('stops listening when it is destroyed', () => {
    const service = TestBed.inject(ConnectivityService);
    TestBed.resetTestingModule();

    window.dispatchEvent(new Event('offline'));

    expect(service.online()).toBe(true);
  });
});
