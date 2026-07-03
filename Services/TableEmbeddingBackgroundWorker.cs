using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class TableEmbeddingBackgroundWorker : BackgroundService
{
    private readonly TableEmbeddingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TableEmbeddingBackgroundWorker> _logger;
    private readonly string _filePath = "Tables.json";
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public TableEmbeddingBackgroundWorker(
        TableEmbeddingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<TableEmbeddingBackgroundWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TableEmbeddingBackgroundWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tableItem = await _queue.DequeueAsync(stoppingToken);

                _logger.LogInformation("Processing Table: {Schema}.{Name}", tableItem.Schema, tableItem.Name);

                var tableText = BuildTableText(tableItem);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
                    var embedding = await embeddingService.GenerateEmbeddingAsync(tableText, stoppingToken);

                    var newEntry = new TableEmbedding
                    {
                        Name = tableItem.Name,
                        Schema = tableItem.Schema,
                        Text = tableText,
                        Embedding = embedding,
                        CreatedAt = DateTime.UtcNow
                    };

                    await SaveEmbeddingAsync(newEntry, stoppingToken);
                }

                _logger.LogInformation("Successfully saved embedding for Table: {Schema}.{Name}. Waiting 5 seconds...", tableItem.Schema, tableItem.Name);

                // Wait 5 seconds before processing the next item
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken is canceled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing Table embedding background work.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("TableEmbeddingBackgroundWorker is stopping.");
    }

    private string BuildTableText(TablePayload table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{table.Schema}].[{table.Name}] (");
        
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var nullability = (col.Nullable?.Equals("NO", StringComparison.OrdinalIgnoreCase) == true) ? "NOT NULL" : "NULL";
            sb.Append($"    [{col.Name}] {col.Type} {nullability}");
            
            if (i < table.Columns.Count - 1)
            {
                sb.AppendLine(",");
            }
            else
            {
                sb.AppendLine();
            }
        }
        sb.AppendLine(")");
        
        return sb.ToString();
    }

    private async Task SaveEmbeddingAsync(TableEmbedding newEntry, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var embeddings = new List<TableEmbedding>();

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    embeddings = JsonSerializer.Deserialize<List<TableEmbedding>>(json) ?? new List<TableEmbedding>();
                }
            }

            // Remove existing entry with the same name if it exists (upsert logic)
            embeddings.RemoveAll(e => e.Schema == newEntry.Schema && e.Name == newEntry.Name);
            embeddings.Add(newEntry);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var newJson = JsonSerializer.Serialize(embeddings, options);

            await File.WriteAllTextAsync(_filePath, newJson, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
