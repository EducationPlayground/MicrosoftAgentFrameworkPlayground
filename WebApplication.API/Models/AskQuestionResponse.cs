namespace WebApplication.API.Models;

public record AskQuestionResponse(string Answer, List<SourceReference> Sources);

public record SourceReference(string DocumentName, int PageNumber, double Relevance);
