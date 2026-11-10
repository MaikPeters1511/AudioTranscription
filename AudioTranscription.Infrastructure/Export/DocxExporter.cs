using AudioTranscription.Domain.Export;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AudioTranscription.Infrastructure.Export;

/// <summary>DOCX export (Open XML SDK, MIT): title and date, a heading per speaker change, body paragraphs.</summary>
public sealed class DocxExporter : ITranscriptExporter
{
    public string Format => "docx";
    public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public string Extension => "docx";

    public byte[] Export(ExportDocument document)
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            AddStyles(main);

            body.Append(StyledParagraph("Heading1", document.Title));
            var date = document.CreatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            body.Append(StyledParagraph("Subtitle", document.Variant is null ? date : $"{date} · {document.Variant}"));

            foreach (var block in document.Blocks)
            {
                if (block.Speaker is not null)
                {
                    var heading = block.StartMs is { } start
                        ? $"{block.Speaker} ({ExportDocumentBuilder.Timestamp(start)})"
                        : block.Speaker;
                    body.Append(StyledParagraph("Heading2", heading));
                }
                else if (block.StartMs is { } time)
                {
                    body.Append(StyledParagraph("Heading2", ExportDocumentBuilder.Timestamp(time)));
                }

                body.Append(new Paragraph(new Run(new Text(block.Text) { Space = SpaceProcessingModeValues.Preserve })));
            }

            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph StyledParagraph(string styleId, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            Style("Subtitle", "Subtitle", 22, bold: false),
            Style("Heading1", "heading 1", 32, bold: true),
            Style("Heading2", "heading 2", 26, bold: true));
        styles.Styles.Save();
    }

    private static Style Style(string id, string name, int halfPoints, bool bold)
    {
        var runProperties = new StyleRunProperties(new FontSize { Val = halfPoints.ToString() });
        if (bold)
            runProperties.PrependChild(new Bold());
        return new Style(new StyleName { Val = name }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, runProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };
    }
}
