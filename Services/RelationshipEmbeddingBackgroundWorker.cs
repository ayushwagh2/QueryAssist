using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class RelationshipEmbeddingBackgroundWorker : BackgroundService
{
    private readonly RelationshipEmbeddingQueue _queue;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<RelationshipEmbeddingBackgroundWorker> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private const string FilePath = "Relationships.json";

    public RelationshipEmbeddingBackgroundWorker(
        RelationshipEmbeddingQueue queue,
        IEmbeddingService embeddingService,
        ILogger<RelationshipEmbeddingBackgroundWorker> logger)
    {
        _queue = queue;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Relationship Embedding Background Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _queue.DequeueAsync(stoppingToken);

            if (workItem != null)
            {
                try
                {
                    await ProcessRelationshipAsync(workItem, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing Relationship work item {ForeignKey}.", workItem.ForeignKey);
                }

                // 1 minute delay to respect API rate limits
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Relationship Embedding Background Worker is stopping.");
    }

    private async Task ProcessRelationshipAsync(RelationshipPayload rel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing embedding for Relationship: {ForeignKey}", rel.ForeignKey);

        var naturalLanguageText = $"Relationship {rel.ForeignKey}: Table {rel.ParentSchema}.{rel.ParentTable} column {rel.ParentColumn} references Table {rel.ReferencedSchema}.{rel.ReferencedTable} column {rel.ReferencedColumn} (On Delete: {rel.OnDelete}, On Update: {rel.OnUpdate}).";

        var embedding = await _embeddingService.GenerateEmbeddingAsync(naturalLanguageText, cancellationToken);

        var newEmbedding = new RelationshipEmbedding
        {
            ForeignKey = rel.ForeignKey,
            Text = naturalLanguageText,
            Embedding = embedding
        };

        await SaveEmbeddingAsync(newEmbedding, cancellationToken);
        
        _logger.LogInformation("Successfully saved embedding for Relationship: {ForeignKey}", rel.ForeignKey);
    }

    private async Task SaveEmbeddingAsync(RelationshipEmbedding newEmbedding, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var relationships = new List<RelationshipEmbedding>();
            if (File.Exists(FilePath))
            {
                var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
                relationships = JsonSerializer.Deserialize<List<RelationshipEmbedding>>(json) ?? new List<RelationshipEmbedding>();
            }

            relationships.RemoveAll(r => r.ForeignKey.Equals(newEmbedding.ForeignKey, StringComparison.OrdinalIgnoreCase));
            relationships.Add(newEmbedding);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var outputJson = JsonSerializer.Serialize(relationships, options);
            await File.WriteAllTextAsync(FilePath, outputJson, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
