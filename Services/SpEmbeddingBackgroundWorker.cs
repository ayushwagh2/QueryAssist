using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class SpEmbeddingBackgroundWorker : BackgroundService
{
    private readonly SpEmbeddingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpEmbeddingBackgroundWorker> _logger;
    private readonly string _filePath = "StoredProcedures.json";
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SpEmbeddingBackgroundWorker(
        SpEmbeddingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<SpEmbeddingBackgroundWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpEmbeddingBackgroundWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var spItem = await _queue.DequeueAsync(stoppingToken);

                _logger.LogInformation("Processing SP: {SpName}", spItem.Name);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
                    var embedding = await embeddingService.GenerateEmbeddingAsync(spItem.Text, stoppingToken);

                    var newEntry = new StoredProcedureEmbedding
                    {
                        Name = spItem.Name,
                        Text = spItem.Text,
                        Embedding = embedding,
                        CreatedAt = DateTime.UtcNow
                    };

                    await SaveEmbeddingAsync(newEntry, stoppingToken);
                }

                _logger.LogInformation("Successfully saved embedding for SP: {SpName}. Waiting 1 minutes...", spItem.Name);

                // Wait 4 minutes before processing the next item
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken is canceled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing SP embedding background work.");
                // Add a short delay to prevent tight loop on persistent failures
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("SpEmbeddingBackgroundWorker is stopping.");
    }

    private async Task SaveEmbeddingAsync(StoredProcedureEmbedding newEntry, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var embeddings = new List<StoredProcedureEmbedding>();

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    embeddings = JsonSerializer.Deserialize<List<StoredProcedureEmbedding>>(json) ?? new List<StoredProcedureEmbedding>();
                }
            }

            // Remove existing entry with the same name if it exists (upsert logic)
            embeddings.RemoveAll(e => e.Name == newEntry.Name);
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
