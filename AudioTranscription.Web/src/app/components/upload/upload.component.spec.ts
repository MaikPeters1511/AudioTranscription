import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
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

  function render(options: object | null = transcriptionOptions, glossaries: object[] = []) {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    const request = http.expectOne('/api/transcription-options');
    if (options) {
      request.flush(options);
    } else {
      request.flush(null, { status: 500, statusText: 'Server Error' });
    }
    http.expectOne('/api/glossaries').flush(glossaries);
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

  it('offers the glossaries and sends the chosen one with the upload (S22)', async () => {
    const fixture = render(transcriptionOptions, [{ id: 'g1', name: 'Projekt Alpha', terms: ['A'], estimatedTokens: 1, maxTokens: 224 }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#upload-glossary option:nth-child(2)')?.textContent).toContain('Projekt Alpha');
    choose(fixture, 'upload-glossary', 'g1');
    selectFile(fixture, new File(['ID3'], 'meeting.mp3', { type: 'audio/mpeg' }));

    expect((http.expectOne('/api/audio-jobs').request.body as FormData).get('glossaryId')).toBe('g1');
  });

  it('hides the glossary select when the user has no glossary (S22)', async () => {
    const fixture = render();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#upload-glossary')).toBeNull();
  });

  // --- S34: several files at once ------------------------------------------

  function selectFiles(fixture: ReturnType<typeof render>, files: File[]) {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    Object.defineProperty(input, 'files', { value: files });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const mp3 = (name: string) => new File(['ID3'], name, { type: 'audio/mpeg' });

  it('lets the file picker select several files', () => {
    const fixture = render();

    expect(fixture.nativeElement.querySelector('input[type=file]').multiple).toBe(true);
  });

  it('uploads several files as a batch with the chosen settings, three at a time', () => {
    const fixture = render();
    choose(fixture, 'upload-model', 'Small');

    selectFiles(fixture, ['a', 'b', 'c', 'd', 'e'].map((n) => mp3(`${n}.mp3`)));

    const posts = http.match('/api/audio-jobs');
    expect(posts).toHaveLength(3);
    expect((posts[0].request.body as FormData).get('model')).toBe('Small');
    expect(fixture.nativeElement.querySelectorAll('[data-testid=batch-item]')).toHaveLength(5);
    posts.forEach((p) => p.flush({ id: 'job' }));
  });

  it('keeps the settings fixed while a batch runs', () => {
    const fixture = render();

    selectFiles(fixture, [mp3('a.mp3'), mp3('b.mp3')]);

    expect(fixture.nativeElement.querySelector('#upload-model').disabled).toBe(true);
    http.match('/api/audio-jobs').forEach((p) => p.flush({ id: 'job' }));
  });

  it('takes several dropped files as a batch', () => {
    const fixture = render();
    const zone: HTMLElement = fixture.nativeElement.querySelector('.border-dashed');
    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [mp3('a.mp3'), mp3('b.mp3')] } });

    zone.dispatchEvent(drop);
    fixture.detectChanges();

    expect(http.match('/api/audio-jobs')).toHaveLength(2);
    expect(fixture.nativeElement.querySelectorAll('[data-testid=batch-item]')).toHaveLength(2);
  });

  it('returns to the drop zone after the batch when asked to upload more', () => {
    const fixture = render();
    selectFiles(fixture, [mp3('a.mp3'), mp3('b.mp3')]);
    http.match('/api/audio-jobs').forEach((p) => p.flush({ id: 'job' }));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid=batch-more]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid=batch-item]')).toBeNull();
    expect(fixture.nativeElement.querySelector('input[type=file]')).not.toBeNull();
  });

  it('still opens the job when exactly one file is selected', () => {
    const fixture = render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    selectFiles(fixture, [mp3('a.mp3')]);

    expect(fixture.nativeElement.querySelector('[data-testid=batch-item]')).toBeNull();
    http.expectOne('/api/audio-jobs').flush({ id: 'job' });
    expect(navigate).toHaveBeenCalledWith(['/jobs', 'job']);
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
    http.expectOne('/api/glossaries').flush([]);
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
    http.expectOne('/api/glossaries').flush([]);
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
