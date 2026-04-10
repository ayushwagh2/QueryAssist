namespace QueryAssist.Services;

public interface ISqlExecutorService
{
    Task<string> ExecuteSelectAsJsonAsync(string query, CancellationToken cancellationToken);
}
