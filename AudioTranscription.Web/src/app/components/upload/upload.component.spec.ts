import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { UploadComponent } from './upload.component';
import { translocoTesting } from '../../i18n/transloco-testing';

const transcriptionOptions = {
  models: ['Tiny', 'Base', 'Small'],
  defaultModel: 'Base',
  languages: ['de', 'en'],
};

describe('UploadComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UploadComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function render(options: object | null = transcriptionOptions) {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    const request = http.expectOne('/api/transcription-options');
    if (options) {
      request.flush(options);
    } else {
      request.flush(null, { status: 500, statusText: 'Server Error' });
    }
    fixture.detectChanges();
    return fixture;
  }

  function selectFile(fixture: ReturnType<typeof render>, file: File) {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function choose(fixture: ReturnType<typeof render>, id: string, value: string) {
    const select: HTMLSelectElement = fixture.nativeElement.querySelector(`#${id}`);
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('renders texts in the active language', () => {
    const fixture = render();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Upload audio');
    expect(text).toContain('Drag an audio file here');
  });

  it('shows a translated validation error for unsupported files', () => {
    const fixture = render();

    selectFile(fixture, new File(['x'], 'notes.txt', { type: 'text/plain' }));

    expect(fixture.nativeElement.textContent).toContain(
      'Invalid file format. Allowed: MP3, WAV, M4A, OGG.',
    );
  });

  it('offers labelled selects for model and language with server default and detection preselected', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;

    const model = el.querySelector<HTMLSelectElement>('#upload-model')!;
    const language = el.querySelector<HTMLSelectElement>('#upload-language')!;
    expect(el.querySelector('label[for="upload-model"]')!.textContent).toContain('Model');
    expect(el.querySelector('label[for="upload-language"]')!.textContent).toContain('Language');
    expect([...model.options].map((o) => o.value)).toEqual(['Tiny', 'Base', 'Small']);
    expect(model.value).toBe('Base');
    expect([...language.options].map((o) => o.textContent!.trim())).toEqual([
      'Detect automatically',
      'German',
      'English',
    ]);
    expect(language.value).toBe('auto');
  });

  it('uploads with the chosen model and language', () => {
    const fixture = render();
    choose(fixture, 'upload-model', 'Small');
    choose(fixture, 'upload-language', 'de');

    selectFile(fixture, new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect(body.get('model')).toBe('Small');
    expect(body.get('language')).toBe('de');
  });

  it('uploads with server defaults when the options cannot be loaded', () => {
    const fixture = render(null);

    expect(fixture.nativeElement.querySelector('#upload-model')).toBeNull();
    selectFile(fixture, new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect(body.has('model')).toBe(false);
    expect(body.has('language')).toBe(false);
  });
});
