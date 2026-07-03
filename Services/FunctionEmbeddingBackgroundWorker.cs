using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class FunctionEmbeddingBackgroundWorker : BackgroundService
{
    private readonly FunctionEmbeddingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FunctionEmbeddingBackgroundWorker> _logger;
    private readonly string _filePath = "Functions.json";
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public FunctionEmbeddingBackgroundWorker(
        FunctionEmbeddingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<FunctionEmbeddingBackgroundWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FunctionEmbeddingBackgroundWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var functionItem = await _queue.DequeueAsync(stoppingToken);

                _logger.LogInformation("Processing Function: {Schema}.{Name}", functionItem.Schema, functionItem.Name);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
                    
                    // We embed the full SQL definition
                    var embedding = await embeddingService.GenerateEmbeddingAsync(functionItem.Definition, stoppingToken);

                    var newEntry = new FunctionEmbedding
                    {
                        Name = functionItem.Name,
                        Schema = functionItem.Schema,
                        Text = functionItem.Definition,
                        Embedding = embedding,
                        CreatedAt = DateTime.UtcNow
                    };

                    await SaveEmbeddingAsync(newEntry, stoppingToken);
                }

                _logger.LogInformation("Successfully saved embedding for Function: {Schema}.{Name}. Waiting 1 minutes...", functionItem.Schema, functionItem.Name);

                // Wait 1 minute before processing the next item
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken is canceled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing Function embedding background work.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        _logger.LogInformation("FunctionEmbeddingBackgroundWorker is stopping.");
    }

    private async Task SaveEmbeddingAsync(FunctionEmbedding newEntry, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var embeddings = new List<FunctionEmbedding>();

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    embeddings = JsonSerializer.Deserialize<List<FunctionEmbedding>>(json) ?? new List<FunctionEmbedding>();
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
