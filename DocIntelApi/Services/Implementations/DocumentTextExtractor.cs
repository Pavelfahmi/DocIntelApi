using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocIntelApi.Services.Interfaces;
using UglyToad.PdfPig;

namespace DocIntelApi.Services.Implementations;

public class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".log", ".json", ".xml", ".html", ".htm", ".rtf"
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".txt", ".md", ".markdown", ".csv", ".log", ".json", ".xml", ".html", ".htm", ".rtf"
    };

    public bool CanExtract(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".doc", StringComparison.OrdinalIgnoreCase))
            return true; // accepted so we can return a clear conversion message

        return !string.IsNullOrWhiteSpace(ext) && SupportedExtensions.Contains(ext);
    }

    public string ExtractText(Stream stream, string fileName)
    {
        if (!stream.CanSeek)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return ExtractText(ms, fileName);
        }

        stream.Position = 0;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext == ".doc")
        {
            throw new InvalidOperationException(
                "Legacy .doc files are not supported. Open the file in Word and Save As .docx, then upload again.");
        }

        var text = ext switch
        {
            ".pdf" => ExtractPdf(stream),
            ".docx" => ExtractDocx(stream),
            _ when PlainTextExtensions.Contains(ext) => ExtractPlainText(stream, ext),
            _ => throw new InvalidOperationException(
                $"Unsupported file type '{ext}'. Supported: PDF, DOCX, TXT, MD, CSV, JSON, XML, HTML, RTF, LOG.")
        };

        return text.Trim();
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        return string.Join(
            Environment.NewLine,
            pdf.GetPages().Select(p => p.Text));
    }

    private static string ExtractDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("DOCX has no document body.");

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            var line = para.InnerText;
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line);
        }

        foreach (var table in body.Elements<Table>())
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(c => c.InnerText.Trim())
                    .Where(t => t.Length > 0);
                var line = string.Join(" | ", cells);
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private static string ExtractPlainText(Stream stream, string ext)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        var text = reader.ReadToEnd();

        if (ext is ".html" or ".htm")
            text = StripHtml(text);

        return text;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var noScript = System.Text.RegularExpressions.Regex.Replace(
            html,
            "<script[\\s\\S]*?</script>",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        noScript = System.Text.RegularExpressions.Regex.Replace(
            noScript,
            "<style[\\s\\S]*?</style>",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var noTags = System.Text.RegularExpressions.Regex.Replace(noScript, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(noTags);
    }
}
