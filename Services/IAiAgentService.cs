namespace QueryAssist.Services;

public interface IAiAgentService
{
    Task<string> AnalyzeQueryAsync(string query, CancellationToken cancellationToken);
}
