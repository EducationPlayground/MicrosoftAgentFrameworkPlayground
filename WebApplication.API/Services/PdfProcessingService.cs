using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace WebApplication.API.Services;

public class PdfProcessingService
{
    private const int MaxTokensPerChunk = 250;
    private const int OverlapTokens = 50;

    private static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

    /// <summary>
    /// Extracts text from a PDF stream, builds an <see cref="IngestionDocument"/> per page,
    /// and splits it into token-based chunks using Microsoft.Extensions.DataIngestion.
    /// </summary>
    public async Task<(int PageCount, List<(string Content, int PageNumber)> Chunks)> ExtractChunksAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        var allChunks = new List<(string Content, int PageNumber)>();

        IngestionChunker<string> chunker = new DocumentTokenChunker(new IngestionChunkerOptions(Tokenizer)
        {
            MaxTokensPerChunk = MaxTokensPerChunk,
            OverlapTokens = OverlapTokens
        });

        using var document = PdfDocument.Open(pdfStream);
        var pageCount = document.NumberOfPages;

        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var ingestionDocument = new IngestionDocument($"page-{page.Number}");
            var section = new IngestionDocumentSection { PageNumber = page.Number };
            section.Elements.Add(new IngestionDocumentParagraph(text.Trim()) { PageNumber = page.Number });
            ingestionDocument.Sections.Add(section);

            await foreach (IngestionChunk<string> chunk in chunker.ProcessAsync(ingestionDocument, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(chunk.Content))
                    allChunks.Add((chunk.Content, page.Number));
            }
        }

        return (pageCount, allChunks);
    }
}
