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
  postProcessingEnabled: false,
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
      'Invalid file format. Allowed: MP3, WAV, M4A, OGG, WebM, MP4, MKV, MOV.',
    );
  });

  it('uploads an MP4 video file (S14)', () => {
    const fixture = render();

    selectFile(fixture, new File(['ftyp'], 'meeting.mp4', { type: 'video/mp4' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect((body.get('file') as File).name).toBe('meeting.mp4');
  });

  it('rejects files above the 500 MB limit (S14)', () => {
    const fixture = render();
    const oversized = new File([new Uint8Array(1)], 'huge.mp3', { type: 'audio/mpeg' });
    Object.defineProperty(oversized, 'size', { value: 500_000_001 });

    selectFile(fixture, oversized);

    expect(fixture.nativeElement.textContent).toContain('Maximum: 500 MB.');
    http.expectNone('/api/audio-jobs');
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

describe('UploadComponent browser recording (S12)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UploadComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function render() {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    http.expectOne('/api/transcription-options').flush(transcriptionOptions);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the drop zone by default and the recorder after switching tabs', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('app-recorder')).toBeNull();
    [...el.querySelectorAll<HTMLButtonElement>('[role=tab]')].find((t) => t.textContent?.includes('Record audio'))!.click();
    fixture.detectChanges();

    expect(el.querySelector('input[type=file]')).toBeNull();
    expect(el.querySelector('app-recorder')).not.toBeNull();
  });

  it('uploads a recorded take like a picked file', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;
    [...el.querySelectorAll<HTMLButtonElement>('[role=tab]')].find((t) => t.textContent?.includes('Record audio'))!.click();
    fixture.detectChanges();

    fixture.componentInstance.onRecorded(new File(['x'], 'recording.webm', { type: 'audio/webm' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect((body.get('file') as File).name).toBe('recording.webm');
  });
});

describe('UploadComponent speaker diarization (S11)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UploadComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function render(options: object) {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    http.expectOne('/api/transcription-options').flush(options);
    fixture.detectChanges();
    return fixture;
  }

  function selectFile(fixture: ReturnType<typeof render>, file: File) {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('hides the diarize checkbox when diarization is not configured on the server', () => {
    const fixture = render({ ...transcriptionOptions, diarizationEnabled: false });

    expect(fixture.nativeElement.querySelector('#upload-diarize')).toBeNull();
  });

  it('offers a diarize checkbox when diarization is configured', () => {
    const fixture = render({ ...transcriptionOptions, diarizationEnabled: true });
    const el: HTMLElement = fixture.nativeElement;

    const checkbox = el.querySelector<HTMLInputElement>('#upload-diarize')!;
    expect(checkbox).not.toBeNull();
    expect(checkbox.checked).toBe(false);
    expect(el.querySelector('label[for="upload-diarize"]')!.textContent).toContain('speaker');
  });

  it('sends diarize=true when the checkbox is checked', () => {
    const fixture = render({ ...transcriptionOptions, diarizationEnabled: true });
    const checkbox: HTMLInputElement = fixture.nativeElement.querySelector('#upload-diarize');
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    selectFile(fixture, new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect(body.get('diarize')).toBe('true');
  });

  it('does not send diarize when the checkbox is left unchecked', () => {
    const fixture = render({ ...transcriptionOptions, diarizationEnabled: true });

    selectFile(fixture, new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' }));

    const body = http.expectOne('/api/audio-jobs').request.body as FormData;
    expect(body.has('diarize')).toBe(false);
  });
});
