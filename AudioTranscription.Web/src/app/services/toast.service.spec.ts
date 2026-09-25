import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('adds a toast with the given message and type', () => {
    service.show('Gespeichert', 'success');

    expect(service.toasts()).toHaveLength(1);
    expect(service.toasts()[0]).toMatchObject({ message: 'Gespeichert', type: 'success' });
  });

  it('removes the toast after its duration', () => {
    service.show('Hinweis', 'info', 1000);

    vi.advanceTimersByTime(999);
    expect(service.toasts()).toHaveLength(1);

    vi.advanceTimersByTime(1);
    expect(service.toasts()).toHaveLength(0);
  });

  it('keeps error toasts visible longer than regular toasts', () => {
    service.error('Fehler');
    service.success('OK');

    vi.advanceTimersByTime(3000);
    expect(service.toasts().map((t) => t.type)).toEqual(['error']);

    vi.advanceTimersByTime(2000);
    expect(service.toasts()).toHaveLength(0);
  });

  it('removes a specific toast by id', () => {
    service.show('eins');
    service.show('zwei');
    const [first] = service.toasts();

    service.remove(first.id);

    expect(service.toasts().map((t) => t.message)).toEqual(['zwei']);
  });
});
