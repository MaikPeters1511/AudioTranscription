using System.Text;
using System.Text.RegularExpressions;

namespace AudioTranscription.Domain.Export;

/// <summary>Markdown export: title, date, then one paragraph per block with speaker and time in a bold/code lead line.</summary>
public sealed partial class MarkdownExporter : ITranscriptExporter
{
    public string Format => "md";
    public string ContentType => "text/markdown; charset=utf-8";
    public string Extension => "md";

    public byte[] Export(ExportDocument document)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Escape(document.Title)).Append("\n\n");
        var date = document.CreatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        sb.Append('*').Append(document.Variant is null ? date : $"{date} · {Escape(document.Variant)}").Append("*\n");

        foreach (var block in document.Blocks)
        {
            sb.Append('\n');
            var lead = new List<string>();
            if (block.Speaker is not null)
                lead.Add($"**{Escape(block.Speaker)}**");
            if (block.StartMs is { } start)
                lead.Add($"`{ExportDocumentBuilder.Timestamp(start)}`");
            if (lead.Count > 0)
                sb.Append(string.Join(' ', lead)).Append("\n\n");
            sb.Append(Escape(block.Text.Trim(), escapeLeadingMarker: true)).Append('\n');
        }

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string Escape(string text, bool escapeLeadingMarker = false)
    {
        var escaped = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '~')
                escaped.Append('\\');
            escaped.Append(c);
        }

        var result = escaped.ToString();
        if (!escapeLeadingMarker)
            return result;
        if (result.StartsWith("- ") || result.StartsWith("+ "))
            return "\\" + result;
        return LeadingNumber().Replace(result, "$1\\$2");
    }

    [GeneratedRegex(@"^(\d+)([.)])")]
    private static partial Regex LeadingNumber();
}
