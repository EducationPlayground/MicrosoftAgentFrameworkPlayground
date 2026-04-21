namespace WebApplication.API.Models;

public record AskQuestionRequest(string Question, int? DocumentId = null);
