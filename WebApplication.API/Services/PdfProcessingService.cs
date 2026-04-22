using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace WebApplication.API.Services;

public class PdfProcessingService
{
    public (int PageCount, List<PdfPageText> Pages) ExtractPages(Stream pdfStream)
    {
        var pages = new List<PdfPageText>();

        using var document = PdfDocument.Open(pdfStream);
        var pageCount = document.NumberOfPages;

        foreach (Page page in document.GetPages())
        {
            var text = page.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            pages.Add(new PdfPageText(page.Number, text));
        }

        return (pageCount, pages);
    }
}

public record PdfPageText(int PageNumber, string Content);
