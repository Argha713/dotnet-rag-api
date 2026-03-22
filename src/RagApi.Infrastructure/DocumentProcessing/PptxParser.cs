using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace RagApi.Infrastructure.DocumentProcessing;

// Argha - 2026-03-22 - #75 - Static helper for PPTX text extraction; used by DocumentProcessor
internal static class PptxParser
{
    // Argha - 2026-03-22 - #75 - Opens a PPTX stream and extracts each slide as "[Slide N: title]\nbody text"
    public static async Task<string> ExtractFromPptxAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var doc = PresentationDocument.Open(stream, isEditable: false);
            var presentationPart = doc.PresentationPart
                ?? throw new InvalidOperationException("PPTX has no presentation part");

            var sb = new StringBuilder();
            var slideNumber = 0;

            foreach (var slideId in presentationPart.Presentation.SlideIdList?.Cast<SlideId>() ?? [])
            {
                slideNumber++;
                var relId = slideId.RelationshipId?.Value;
                if (relId == null) continue;

                var slidePart = (SlidePart)presentationPart.GetPartById(relId);
                var slideTitle = GetSlideTitle(slidePart);
                var bodyText = GetSlideBodyText(slidePart);

                sb.AppendLine($"[Slide {slideNumber}: {slideTitle}]");
                if (!string.IsNullOrWhiteSpace(bodyText))
                    sb.AppendLine(bodyText);
                sb.AppendLine();
            }

            return sb.ToString();
        }, cancellationToken);
    }

    // Argha - 2026-03-22 - #75 - Extract title from the slide's title shape (placeholder type Title or CenteredTitle)
    private static string GetSlideTitle(SlidePart slidePart)
    {
        var tree = slidePart.Slide.CommonSlideData?.ShapeTree;
        if (tree == null) return string.Empty;

        foreach (var shape in tree.Elements<Shape>())
        {
            var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties
                ?.GetFirstChild<PlaceholderShape>();

            // Argha - 2026-03-22 - #75 - Use equality check; 'is' pattern requires constant but PlaceholderValues are not constants in this context
            var phType = ph?.Type?.Value;
            if (phType == PlaceholderValues.Title || phType == PlaceholderValues.CenteredTitle)
            {
                return shape.TextBody?.InnerText ?? string.Empty;
            }
        }

        return string.Empty;
    }

    // Argha - 2026-03-22 - #75 - Extract non-title text from the slide; joins paragraphs with newlines
    private static string GetSlideBodyText(SlidePart slidePart)
    {
        var tree = slidePart.Slide.CommonSlideData?.ShapeTree;
        if (tree == null) return string.Empty;

        var sb = new StringBuilder();

        foreach (var shape in tree.Elements<Shape>())
        {
            var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties
                ?.GetFirstChild<PlaceholderShape>();

            // Argha - 2026-03-22 - #75 - Skip title placeholders (already captured above)
            // Argha - 2026-03-22 - #75 - Use equality check; 'is' pattern requires constant but PlaceholderValues are not constants in this context
            var bodyPhType = ph?.Type?.Value;
            if (bodyPhType == PlaceholderValues.Title || bodyPhType == PlaceholderValues.CenteredTitle)
                continue;

            var textBody = shape.TextBody;
            if (textBody == null) continue;

            foreach (var paragraph in textBody.Elements<A.Paragraph>())
            {
                var text = paragraph.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }
        }

        return sb.ToString().TrimEnd();
    }
}
