namespace QueryAssist.Services;

public interface IAiAgentService
{
    Task<string> AnalyzeQueryAsync(string query, CancellationToken cancellationToken);
    Task<string> GenerateQueryAsync(string requirement, CancellationToken cancellationToken);
    Task<string> AskQuestionAsync(string question, CancellationToken cancellationToken);
    Task<string> ExplainStoredProcedureAsync(string spName, string spText, CancellationToken cancellationToken);
}
