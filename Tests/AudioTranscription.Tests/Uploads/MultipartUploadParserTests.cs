using System.Diagnostics;
using System.Text;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Api.Uploads;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.Uploads;

public class MultipartUploadParserTests : IDisposable
{
    private const string Boundary = "test-boundary-123";
    private readonly TempDirectory _dir = new();
    private readonly ITempFileStore _tempFileStore;

    public MultipartUploadParserTests()
    {
        _tempFileStore = new TempFileStore(Options.Create(new UploadOptions { TempStoragePath = _dir.Path }));
    }

    private static HttpRequest BuildRequest(Stream body)
    {
        var context = new DefaultHttpContext { Request = { ContentType = $"multipart/form-data; boundary={Boundary}", Body = body } };
        return context.Request;
    }

    private static MemoryStream BuildMultipartBody(byte[] fileBytes, string fileName = "meeting.mp3", string contentType = "audio/mpeg",
        string? model = null, string? language = null, bool? diarize = null)
    {
        var stream = new MemoryStream();
        void WriteLine(string s) => stream.Write(Encoding.ASCII.GetBytes(s + "\r\n"));

        void WriteField(string name, string value)
        {
            WriteLine($"--{Boundary}");
            WriteLine($"Content-Disposition: form-data; name=\"{name}\"");
            WriteLine("");
            WriteLine(value);
        }

        WriteLine($"--{Boundary}");
        WriteLine($"Content-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"");
        WriteLine($"Content-Type: {contentType}");
        WriteLine("");
        stream.Write(fileBytes);
        WriteLine("");

        if (model is not null) WriteField("model", model);
        if (language is not null) WriteField("language", language);
        if (diarize is not null) WriteField("diarize", diarize.Value.ToString());

        stream.Write(Encoding.ASCII.GetBytes($"--{Boundary}--\r\n"));
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task ParseAsync_WithFileAndFields_ExtractsEverything()
    {
        var fileBytes = "ID3some audio bytes"u8.ToArray();
        using var body = BuildMultipartBody(fileBytes, model: "Small", language: "de", diarize: true);

        var result = await MultipartUploadParser.ParseAsync(BuildRequest(body), Guid.NewGuid(), _tempFileStore, 1_000_000, CancellationToken.None);

        result!.FileName.Should().Be("meeting.mp3");
        result.FileContentType.Should().Be("audio/mpeg");
        result.FileSizeBytes.Should().Be(fileBytes.Length);
        result.Model.Should().Be("Small");
        result.Language.Should().Be("de");
        result.Diarize.Should().BeTrue();
        result.FileTooLarge.Should().BeFalse();
        File.ReadAllBytes(result.FilePath!).Should().Equal(fileBytes);
    }

    [Fact]
    public async Task ParseAsync_CapturesTheFirstBytesForMagicByteChecks()
    {
        var fileBytes = "ID3"u8.ToArray().Concat(new byte[100]).ToArray();
        using var body = BuildMultipartBody(fileBytes);

        var result = await MultipartUploadParser.ParseAsync(BuildRequest(body), Guid.NewGuid(), _tempFileStore, 1_000_000, CancellationToken.None);

        result!.HeaderBytes.ToArray().Take(3).Should().Equal("ID3"u8.ToArray());
    }

    [Fact]
    public async Task ParseAsync_WithoutModelOrLanguage_LeavesThemNull()
    {
        using var body = BuildMultipartBody("ID3"u8.ToArray());

        var result = await MultipartUploadParser.ParseAsync(BuildRequest(body), Guid.NewGuid(), _tempFileStore, 1_000_000, CancellationToken.None);

        result!.Model.Should().BeNull();
        result.Language.Should().BeNull();
        result.Diarize.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_WhenFileExceedsTheLimit_StopsWritingAndDeletesThePartialFile()
    {
        var fileBytes = new byte[2_000];
        using var body = BuildMultipartBody(fileBytes);

        var result = await MultipartUploadParser.ParseAsync(BuildRequest(body), Guid.NewGuid(), _tempFileStore, 1_000, CancellationToken.None);

        result!.FileTooLarge.Should().BeTrue();
        result.FilePath.Should().BeNull();
        result.HasFile.Should().BeFalse();
        Directory.GetFiles(_dir.Path).Should().BeEmpty("the partial file must not be left behind");
    }

    [Fact]
    public async Task ParseAsync_WithoutAMultipartBoundary_ReturnsNull()
    {
        var context = new DefaultHttpContext { Request = { ContentType = "application/json", Body = new MemoryStream() } };

        var result = await MultipartUploadParser.ParseAsync(context.Request, Guid.NewGuid(), _tempFileStore, 1_000, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_With400MbBody_DoesNotGrowWorkingSetByMoreThan200Mb()
    {
        // S14-T1 DoD: the file must be streamed to disk, not buffered whole in memory. Generates the
        // multipart body on the fly (no 400 MB byte array, no real HttpClient/TestServer round trip,
        // which buffer the whole request in a single-process test and would confound the measurement).
        const long payloadLength = 400_000_000;
        var body = new GeneratedMultipartBodyStream(Boundary, "large.mp3", "audio/mpeg", payloadLength);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = Process.GetCurrentProcess().WorkingSet64;

        var result = await MultipartUploadParser.ParseAsync(BuildRequest(body), Guid.NewGuid(), _tempFileStore, 500_000_000, CancellationToken.None);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = Process.GetCurrentProcess().WorkingSet64;

        result!.FileSizeBytes.Should().Be(payloadLength);
        new FileInfo(result.FilePath!).Length.Should().Be(payloadLength);
        (after - before).Should().BeLessThan(200_000_000,
            "the parser must stream to disk with a small fixed buffer instead of buffering the request in memory");
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>Generates a valid multipart/form-data body with a zero-filled payload, without allocating it whole.</summary>
    private sealed class GeneratedMultipartBodyStream(string boundary, string fileName, string contentType, long payloadLength) : Stream
    {
        private readonly byte[] _preamble = Encoding.ASCII.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
        private readonly byte[] _epilogue = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        private long _position;

        private long PayloadStart => _preamble.Length;
        private long PayloadEnd => PayloadStart + payloadLength;
        private long TotalLength => PayloadEnd + _epilogue.Length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => TotalLength;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= TotalLength)
                return 0;

            if (_position < _preamble.Length)
            {
                var toCopy = (int)Math.Min(count, _preamble.Length - _position);
                Array.Copy(_preamble, _position, buffer, offset, toCopy);
                _position += toCopy;
                return toCopy;
            }

            if (_position < PayloadEnd)
            {
                var toCopy = (int)Math.Min(count, PayloadEnd - _position);
                Array.Clear(buffer, offset, toCopy);
                _position += toCopy;
                return toCopy;
            }

            var epilogueOffset = (int)(_position - PayloadEnd);
            var toCopyE = (int)Math.Min(count, _epilogue.Length - epilogueOffset);
            Array.Copy(_epilogue, epilogueOffset, buffer, offset, toCopyE);
            _position += toCopyE;
            return toCopyE;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override int Read(Span<byte> buffer)
        {
            var array = new byte[buffer.Length];
            var read = Read(array, 0, array.Length);
            array.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
