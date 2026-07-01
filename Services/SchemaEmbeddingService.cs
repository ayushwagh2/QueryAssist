using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QueryAssist.Models;
using QueryAssist.Options;

namespace QueryAssist.Services;

public sealed class SchemaEmbeddingService : ISchemaEmbeddingService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEmbeddingService _embeddingService;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<SchemaEmbeddingService> _logger;

    private readonly ConcurrentDictionary<string, SchemaEmbedding> _schemaEmbeddings = new();
    private readonly ConcurrentDictionary<string, (float[] Embedding, DateTime CachedAt)> _queryEmbeddingCache = new();
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private DateTime _lastRefresh = DateTime.MinValue;
    private string? _schemaHash;

    // File-based cache for persistence across restarts
    private static readonly string CacheDirectory = Path.Combine(Path.GetTempPath(), "QueryAssist", "EmbeddingCache");
    private static readonly string SchemaCacheFile = Path.Combine(CacheDirectory, "schema_embeddings.json");

    public bool IsInitialized => _schemaEmbeddings.Count > 0;

    public SchemaEmbeddingService(
        IServiceProvider serviceProvider,
        IEmbeddingService embeddingService,
        IOptions<EmbeddingOptions> options,
        ILogger<SchemaEmbeddingService> logger)
    {
        _serviceProvider = serviceProvider;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;

        // Ensure cache directory exists
        Directory.CreateDirectory(CacheDirectory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (IsInitialized && DateTime.UtcNow - _lastRefresh < _options.CacheRefreshInterval)
            {
                _logger.LogInformation("Schema embeddings already initialized and fresh. Skipping.");
                return;
            }

            // Try to load from file cache first
            if (await TryLoadFromFileCacheAsync(cancellationToken))
            {
                _logger.LogInformation("✅ Loaded {Count} schema embeddings from file cache.", _schemaEmbeddings.Count);
                return;
            }

            _logger.LogInformation("Starting schema embeddings initialization...");
            await RefreshEmbeddingsInternalAsync(cancellationToken);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task RefreshEmbeddingsAsync(CancellationToken cancellationToken)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshEmbeddingsInternalAsync(cancellationToken);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<bool> TryLoadFromFileCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(SchemaCacheFile))
            {
                return false;
            }

            var cacheJson = await File.ReadAllTextAsync(SchemaCacheFile, cancellationToken);
            var cache = JsonSerializer.Deserialize<SchemaCacheData>(cacheJson);

            if (cache == null || cache.Embeddings == null || cache.Embeddings.Count == 0)
            {
                return false;
            }

            // Check if cache is still fresh
            if (DateTime.UtcNow - cache.CreatedAt > _options.CacheRefreshInterval)
            {
                _logger.LogInformation("File cache is stale. Will regenerate embeddings.");
                return false;
            }

            // Verify schema hash matches (schema hasn't changed)
            using var scope = _serviceProvider.CreateScope();
            var sqlExecutorService = scope.ServiceProvider.GetRequiredService<ISqlExecutorService>();
            var currentHash = await ComputeSchemaHashAsync(sqlExecutorService, cancellationToken);

            if (cache.SchemaHash != currentHash)
            {
                _logger.LogInformation("Schema has changed since cache was created. Will regenerate embeddings.");
                return false;
            }

            // Load embeddings into memory
            _schemaEmbeddings.Clear();
            foreach (var embedding in cache.Embeddings)
            {
                var key = BuildSchemaKeyFromEmbedding(embedding);
                _schemaEmbeddings[key] = embedding;
            }

            _lastRefresh = cache.CreatedAt;
            _schemaHash = cache.SchemaHash;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load embeddings from file cache. Will regenerate.");
            return false;
        }
    }

    private async Task SaveToFileCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cache = new SchemaCacheData
            {
                CreatedAt = _lastRefresh,
                SchemaHash = _schemaHash,
                Embeddings = _schemaEmbeddings.Values.ToList()
            };

            var cacheJson = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(SchemaCacheFile, cacheJson, cancellationToken);

            _logger.LogInformation("Saved {Count} schema embeddings to file cache.", _schemaEmbeddings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save embeddings to file cache.");
        }
    }

    private static async Task<string> ComputeSchemaHashAsync(ISqlExecutorService sqlExecutorService, CancellationToken cancellationToken)
    {
        // Get a quick hash of table/column structure to detect schema changes
        var hashQuery = """
            SELECT CHECKSUM_AGG(CHECKSUM(TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE))
            FROM INFORMATION_SCHEMA.COLUMNS
            """;

        var result = await sqlExecutorService.ExecuteSelectAsJsonAsync(hashQuery, cancellationToken);
        return result;
    }

    private async Task RefreshEmbeddingsInternalAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing schema embeddings...");

        using var scope = _serviceProvider.CreateScope();
        var sqlExecutorService = scope.ServiceProvider.GetRequiredService<ISqlExecutorService>();

        // Compute schema hash for change detection
        _schemaHash = await ComputeSchemaHashAsync(sqlExecutorService, cancellationToken);

        _logger.LogInformation("Fetching schema metadata from database...");
        var schemaElements = await FetchSchemaMetadataAsync(sqlExecutorService, cancellationToken);

        if (schemaElements.Count == 0)
        {
            _logger.LogWarning("No schema elements found to embed.");
            return;
        }

        _logger.LogInformation("Found {Count} schema elements. Generating enriched descriptions...", schemaElements.Count);

        // Generate ENRICHED descriptions for better retrieval
        var descriptions = schemaElements.Select(BuildEnrichedDescription).ToList();

        _logger.LogInformation("Generating embeddings for {Count} schema elements...", descriptions.Count);

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(descriptions, cancellationToken);

        _logger.LogInformation("Building schema embedding cache...");
        _schemaEmbeddings.Clear();

        for (var i = 0; i < schemaElements.Count; i++)
        {
            var element = schemaElements[i];
            var key = BuildSchemaKey(element);

            var schemaEmbedding = new SchemaEmbedding
            {
                TableSchema = element.TableSchema,
                TableName = element.TableName,
                ColumnName = element.ColumnName,
                DataType = element.DataType,
                Description = descriptions[i],
                Embedding = embeddings[i],
                ElementType = element.ElementType,
                RelatedTables = element.RelatedTables,
                ForeignKeyHints = element.ForeignKeyHints
            };

            _schemaEmbeddings[key] = schemaEmbedding;
        }

        _lastRefresh = DateTime.UtcNow;
        _logger.LogInformation("✅ Schema embeddings refreshed successfully. Total elements: {Count}", _schemaEmbeddings.Count);

        // Save to file cache for persistence
        await SaveToFileCacheAsync(cancellationToken);
    }

    /// <summary>
    /// Builds enriched descriptions with synonyms and context for better semantic matching.
    /// </summary>
    private static string BuildEnrichedDescription(SchemaElement element)
    {
        var sb = new StringBuilder();

        switch (element.ElementType)
        {
            case SchemaElementType.Table:
                sb.Append($"Database table {element.TableSchema}.{element.TableName}");

                // Add semantic hints based on common naming patterns
                var tableName = element.TableName.ToLowerInvariant();
                if (tableName.Contains("application"))
                    sb.Append(" - stores application records, apps, submissions, requests");
                else if (tableName.Contains("user") || tableName.Contains("person") || tableName.Contains("contact"))
                    sb.Append(" - stores user accounts, people, contacts, individuals");
                else if (tableName.Contains("type") || tableName.Contains("category") || tableName.Contains("status"))
                    sb.Append(" - lookup table for types, categories, or status values");
                else if (tableName.Contains("log") || tableName.Contains("audit") || tableName.Contains("history"))
                    sb.Append(" - audit/history/log records");
                else if (tableName.Contains("address") || tableName.Contains("location"))
                    sb.Append(" - address, location, geographic data");
                else if (tableName.Contains("document") || tableName.Contains("file") || tableName.Contains("attachment"))
                    sb.Append(" - documents, files, attachments");
                else if (tableName.Contains("payment") || tableName.Contains("invoice") || tableName.Contains("fee"))
                    sb.Append(" - payment, billing, financial records");
                break;

            case SchemaElementType.Column:
                sb.Append($"Column {element.TableName}.{element.ColumnName} ({element.DataType})");

                if (element.IsPrimaryKey)
                    sb.Append(" PRIMARY KEY identifier");

                // Add semantic hints for common column patterns
                var colName = element.ColumnName?.ToLowerInvariant() ?? "";
                if (colName.Contains("name") || colName.Contains("title") || colName.Contains("description"))
                    sb.Append(" - human-readable name/title/description text");
                else if (colName.Contains("date") || colName.Contains("time") || colName.EndsWith("at") || colName.EndsWith("on"))
                    sb.Append(" - date/time/timestamp field");
                else if (colName.Contains("count") || colName.Contains("total") || colName.Contains("amount") || colName.Contains("quantity"))
                    sb.Append(" - numeric count/total/amount/quantity");
                else if (colName.Contains("email"))
                    sb.Append(" - email address");
                else if (colName.Contains("phone") || colName.Contains("mobile"))
                    sb.Append(" - phone/mobile number");
                else if (colName.Contains("active") || colName.Contains("enabled") || colName.Contains("deleted") || colName.Contains("archived"))
                    sb.Append(" - boolean flag/status indicator");
                else if (colName.Contains("created") || colName.Contains("modified") || colName.Contains("updated"))
                    sb.Append(" - audit timestamp for record creation/modification");

                if (element.ForeignKeyHints?.Count > 0)
                {
                    sb.Append($" FOREIGN KEY: {string.Join(", ", element.ForeignKeyHints)}");
                }
                break;

            case SchemaElementType.Relationship:
                sb.Append($"Relationship: {element.TableName}.{element.ColumnName}");
                sb.Append($" references/joins to {string.Join(", ", element.RelatedTables ?? [])}");
                sb.Append(" - use for JOINs between these tables");
                break;
        }

        return sb.ToString();
    }

    private static async Task<List<SchemaElement>> FetchSchemaMetadataAsync(
        ISqlExecutorService sqlExecutorService,
        CancellationToken cancellationToken)
    {
        var elements = new List<SchemaElement>();

        // Fetch table-level schema
        var tablesQuery = """
            SELECT DISTINCT
                TABLE_SCHEMA,
                TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;

        var tablesJson = await sqlExecutorService.ExecuteSelectAsJsonAsync(tablesQuery, cancellationToken);
        var tables = JsonSerializer.Deserialize<List<TableInfo>>(tablesJson) ?? [];

        foreach (var table in tables)
        {
            elements.Add(new SchemaElement
            {
                TableSchema = table.TABLE_SCHEMA ?? "dbo",
                TableName = table.TABLE_NAME ?? "",
                ElementType = SchemaElementType.Table
            });
        }

        // Fetch column-level schema with enhanced metadata
        var columnsQuery = """
            SELECT
                c.TABLE_SCHEMA,
                c.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                c.CHARACTER_MAXIMUM_LENGTH,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PRIMARY_KEY
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON c.TABLE_SCHEMA = pk.TABLE_SCHEMA
                AND c.TABLE_NAME = pk.TABLE_NAME
                AND c.COLUMN_NAME = pk.COLUMN_NAME
            ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
            """;

        var columnsJson = await sqlExecutorService.ExecuteSelectAsJsonAsync(columnsQuery, cancellationToken);
        var columns = JsonSerializer.Deserialize<List<ColumnInfo>>(columnsJson) ?? [];

        foreach (var column in columns)
        {
            var foreignKeyHints = DetectForeignKeyHints(column.COLUMN_NAME ?? "", tables);

            elements.Add(new SchemaElement
            {
                TableSchema = column.TABLE_SCHEMA ?? "dbo",
                TableName = column.TABLE_NAME ?? "",
                ColumnName = column.COLUMN_NAME,
                DataType = column.DATA_TYPE,
                IsNullable = column.IS_NULLABLE == "YES",
                IsPrimaryKey = column.IS_PRIMARY_KEY == 1,
                ElementType = SchemaElementType.Column,
                ForeignKeyHints = foreignKeyHints
            });
        }

        var relationships = DetectRelationships(columns, tables);
        elements.AddRange(relationships);

        return elements;
    }

    private static List<string> DetectForeignKeyHints(string columnName, List<TableInfo> tables)
    {
        var hints = new List<string>();

        if (columnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase) && columnName.Length > 2)
        {
            var potentialTable = columnName[..^2];
            if (tables.Any(t => t.TABLE_NAME?.Equals(potentialTable, StringComparison.OrdinalIgnoreCase) == true))
            {
                hints.Add($"References {potentialTable} table");
            }
        }

        if (columnName.EndsWith("TypeID", StringComparison.OrdinalIgnoreCase))
            hints.Add("Lookup/type reference - join to get readable name");

        if (columnName.EndsWith("StatusID", StringComparison.OrdinalIgnoreCase))
            hints.Add("Status reference - join to get status name");

        return hints;
    }

    private static List<SchemaElement> DetectRelationships(List<ColumnInfo> columns, List<TableInfo> tables)
    {
        var relationships = new List<SchemaElement>();
        var tableNames = tables.Select(t => t.TABLE_NAME).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fkColumns = columns
            .Where(c => c.COLUMN_NAME?.EndsWith("ID", StringComparison.OrdinalIgnoreCase) == true
                        && c.COLUMN_NAME.Length > 2)
            .GroupBy(c => new { c.TABLE_SCHEMA, c.TABLE_NAME });

        foreach (var tableGroup in fkColumns)
        {
            foreach (var column in tableGroup)
            {
                var potentialRefTable = column.COLUMN_NAME![..^2];
                if (tableNames.Contains(potentialRefTable))
                {
                    relationships.Add(new SchemaElement
                    {
                        TableSchema = tableGroup.Key.TABLE_SCHEMA ?? "dbo",
                        TableName = tableGroup.Key.TABLE_NAME ?? "",
                        ColumnName = column.COLUMN_NAME,
                        ElementType = SchemaElementType.Relationship,
                        RelatedTables = [potentialRefTable],
                        ForeignKeyHints = [$"{tableGroup.Key.TABLE_NAME}.{column.COLUMN_NAME} -> {potentialRefTable}"]
                    });
                }
            }
        }

        return relationships;
    }

    private static string BuildSchemaKey(SchemaElement element)
    {
        return element.ElementType == SchemaElementType.Column
            ? $"{element.TableSchema}.{element.TableName}.{element.ColumnName}"
            : $"{element.TableSchema}.{element.TableName}:{element.ElementType}";
    }

    private static string BuildSchemaKeyFromEmbedding(SchemaEmbedding embedding)
    {
        return embedding.ElementType == SchemaElementType.Column
            ? $"{embedding.TableSchema}.{embedding.TableName}.{embedding.ColumnName}"
            : $"{embedding.TableSchema}.{embedding.TableName}:{embedding.ElementType}";
    }

    public async Task<IReadOnlyList<SchemaSearchResult>> SearchRelevantSchemaAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            _logger.LogInformation("Schema embeddings not initialized. Initializing now...");
            await InitializeAsync(cancellationToken);
        }

        // Check query embedding cache
        var queryHash = ComputeQueryHash(query);
        float[] queryEmbedding;

        if (_queryEmbeddingCache.TryGetValue(queryHash, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(30))
        {
            queryEmbedding = cached.Embedding;
            _logger.LogDebug("Using cached query embedding for: {Query}", query);
        }
        else
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            _queryEmbeddingCache[queryHash] = (queryEmbedding, DateTime.UtcNow);

            // Limit cache size
            if (_queryEmbeddingCache.Count > 100)
            {
                var oldestKeys = _queryEmbeddingCache
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(20)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldestKeys)
                {
                    _queryEmbeddingCache.TryRemove(key, out _);
                }
            }
        }

        // Multi-stage retrieval for better accuracy
        var results = _schemaEmbeddings.Values
            .Select(schema => new SchemaSearchResult
            {
                Schema = schema,
                SimilarityScore = _embeddingService.CosineSimilarity(queryEmbedding, schema.Embedding)
            })
            .Where(r => r.SimilarityScore >= _options.MinSimilarityThreshold)
            .OrderByDescending(r => r.SimilarityScore)
            .ToList();

        // Boost tables that have matching columns (relationship-aware retrieval)
        var topTables = results
            .Where(r => r.Schema.ElementType == SchemaElementType.Table)
            .Take(5)
            .Select(r => r.Schema.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Include related columns from top tables even if similarity is slightly lower
        var relatedColumns = _schemaEmbeddings.Values
            .Where(s => s.ElementType == SchemaElementType.Column && topTables.Contains(s.TableName))
            .Select(schema => new SchemaSearchResult
            {
                Schema = schema,
                SimilarityScore = _embeddingService.CosineSimilarity(queryEmbedding, schema.Embedding) + 0.05f // Small boost
            })
            .Where(r => r.SimilarityScore >= _options.MinSimilarityThreshold * 0.8f);

        var combinedResults = results
            .Concat(relatedColumns)
            .GroupBy(r => BuildSchemaKeyFromEmbedding(r.Schema))
            .Select(g => g.OrderByDescending(r => r.SimilarityScore).First())
            .OrderByDescending(r => r.SimilarityScore)
            .Take(maxResults)
            .ToList();

        _logger.LogInformation(
            "Schema search for '{Query}' found {Count} results (threshold: {Threshold})",
            query.Length > 50 ? query[..50] + "..." : query,
            combinedResults.Count,
            _options.MinSimilarityThreshold);

        return combinedResults;
    }

    private static string ComputeQueryHash(string query)
    {
        var bytes = Encoding.UTF8.GetBytes(query.ToLowerInvariant().Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash)[..16];
    }

    public void Dispose()
    {
        _initializationLock.Dispose();
    }

    // Cache DTOs
    private sealed class SchemaCacheData
    {
        public DateTime CreatedAt { get; set; }
        public string? SchemaHash { get; set; }
        public List<SchemaEmbedding>? Embeddings { get; set; }
    }

    private sealed class TableInfo
    {
        public string? TABLE_SCHEMA { get; set; }
        public string? TABLE_NAME { get; set; }
    }

    private sealed class ColumnInfo
    {
        public string? TABLE_SCHEMA { get; set; }
        public string? TABLE_NAME { get; set; }
        public string? COLUMN_NAME { get; set; }
        public string? DATA_TYPE { get; set; }
        public string? IS_NULLABLE { get; set; }
        public int? CHARACTER_MAXIMUM_LENGTH { get; set; }
        public int IS_PRIMARY_KEY { get; set; }
    }

    private sealed class SchemaElement
    {
        public required string TableSchema { get; init; }
        public required string TableName { get; init; }
        public string? ColumnName { get; init; }
        public string? DataType { get; init; }
        public bool IsNullable { get; init; }
        public bool IsPrimaryKey { get; init; }
        public SchemaElementType ElementType { get; init; }
        public List<string>? RelatedTables { get; init; }
        public List<string>? ForeignKeyHints { get; init; }
    }
}