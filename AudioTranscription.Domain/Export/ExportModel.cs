namespace AudioTranscription.Domain.Export;

/// <summary>Which optional parts an export contains (S21).</summary>
/// <param name="Timestamps">Start time per block.</param>
/// <param name="Speakers">Speaker name per block; consecutive segments of one speaker are merged.</param>
public sealed record ExportOptions(bool Timestamps = true, bool Speakers = true);

/// <summary>A paragraph of an export: optional speaker and start time plus text.</summary>
public sealed record ExportBlock(string? Speaker, long? StartMs, string Text);

/// <summary>Format-neutral content of an export. Exporters only render it; options are already applied.</summary>
/// <param name="Variant">Label of the post-processed variant (S10), null for the raw transcript.</param>
public sealed record ExportDocument(string Title, DateTime CreatedAtUtc, string? Variant, IReadOnlyList<ExportBlock> Blocks);

/// <summary>Renders an <see cref="ExportDocument"/> into the bytes of one file format.</summary>
public interface ITranscriptExporter
{
    /// <summary>Value of the <c>format</c> query parameter, lower case.</summary>
    string Format { get; }
    string ContentType { get; }
    string Extension { get; }
    byte[] Export(ExportDocument document);
}
