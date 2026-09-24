import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { UploadComponent } from './upload.component';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('UploadComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UploadComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('renders texts in the active language', () => {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Upload audio');
    expect(text).toContain('Drag an audio file here');
  });

  it('shows a translated validation error for unsupported files', () => {
    const fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    const file = new File(['x'], 'notes.txt', { type: 'text/plain' });
    Object.defineProperty(input, 'files', { value: [file] });

    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Invalid file format. Allowed: MP3, WAV, M4A, OGG.',
    );
  });
});
