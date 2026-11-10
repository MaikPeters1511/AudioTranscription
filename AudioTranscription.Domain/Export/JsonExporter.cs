using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioTranscription.Domain.Export;

/// <summary>
/// JSON export with a versioned schema: <c>{ schemaVersion, title, createdAtUtc, variant?, blocks: [{ speaker?, startMs?, text }] }</c>.
/// Fields that were switched off by the export options are omitted. Bump <see cref="SchemaVersion"/> on breaking changes.
/// </summary>
public sealed class JsonExporter : ITranscriptExporter
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public string Format => "json";
    public string ContentType => "application/json; charset=utf-8";
    public string Extension => "json";

    public byte[] Export(ExportDocument document) => JsonSerializer.SerializeToUtf8Bytes(new Payload(
        SchemaVersion, document.Title, document.CreatedAtUtc, document.Variant,
        document.Blocks.Select(b => new BlockPayload(b.Speaker, b.StartMs, b.Text)).ToList()), Options);

    private sealed record Payload(int SchemaVersion, string Title, DateTime CreatedAtUtc, string? Variant, List<BlockPayload> Blocks);

    private sealed record BlockPayload(string? Speaker, long? StartMs, string Text);
}
