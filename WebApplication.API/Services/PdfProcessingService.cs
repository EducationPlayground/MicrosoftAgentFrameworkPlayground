using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace WebApplication.API.Services;

public class PdfProcessingService
{
    private const int MaxChunkSize = 1000;
    private const int OverlapSize = 200;

    public (int PageCount, List<(string Content, int PageNumber)> Chunks) ExtractChunks(Stream pdfStream)
    {
        var allChunks = new List<(string Content, int PageNumber)>();

        using var document = PdfDocument.Open(pdfStream);
        var pageCount = document.NumberOfPages;

        foreach (Page page in document.GetPages())
        {
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var chunks = SplitIntoChunks(text.Trim());
            foreach (var chunk in chunks)
            {
                allChunks.Add((chunk, page.Number));
            }
        }

        return (pageCount, allChunks);
    }

    private static List<string> SplitIntoChunks(string text)
    {
        var chunks = new List<string>();
        if (text.Length <= MaxChunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(MaxChunkSize, text.Length - start);
            var chunk = text.Substring(start, length);
            chunks.Add(chunk);
            start += MaxChunkSize - OverlapSize;
        }

        return chunks;
    }
}
