using AudioTranscription.Domain.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AudioTranscription.Infrastructure.Export;

/// <summary>
/// PDF export (QuestPDF, Community license: free below 1 M USD annual revenue, see docs/adr/0008).
/// A4 with header (title, date), automatic page breaks and page numbers; Lato is bundled, so umlauts render without system fonts.
/// </summary>
public sealed class PdfExporter : ITranscriptExporter
{
    static PdfExporter() => QuestPDF.Settings.License = LicenseType.Community;

    public string Format => "pdf";
    public string ContentType => "application/pdf";
    public string Extension => "pdf";

    public byte[] Export(ExportDocument document)
    {
        var date = document.CreatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var subtitle = document.Variant is null ? date : $"{date} · {document.Variant}";

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(11));

            page.Header().PaddingBottom(10).Column(column =>
            {
                column.Item().Text(document.Title).FontSize(16).Bold();
                column.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            page.Content().Column(column =>
            {
                column.Spacing(8);
                foreach (var block in document.Blocks)
                {
                    column.Item().Column(item =>
                    {
                        var lead = string.Join("  ", new[]
                        {
                            block.Speaker,
                            block.StartMs is { } start ? ExportDocumentBuilder.Timestamp(start) : null,
                        }.Where(part => part is not null));
                        if (lead.Length > 0)
                            item.Item().Text(lead).Bold().FontColor(Colors.Grey.Darken3);
                        item.Item().Text(block.Text);
                    });
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Darken1));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }
}
