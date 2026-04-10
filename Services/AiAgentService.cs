using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using QueryAssist.Models;
using QueryAssist.Options;
using QueryAssist.Utilities;

namespace QueryAssist.Services;

public sealed class AiAgentService : IAiAgentService
{
    private const string SystemPrompt = """
        You are a database assistant.

        You will be given a SQL data-change query such as UPDATE or INSERT ... SELECT.

        Your job is to explain the impact of this query in plain English.

        You are allowed to call the run_sql tool to fetch additional data.

        Rules:
        * Before reasoning about table structure, use the provided schema snapshot from INFORMATION_SCHEMA.COLUMNS
        * Never assume a table or column exists unless it appears in the schema snapshot or you verify it with run_sql
        * Only generate SELECT queries
        * Prefer one SELECT statement per run_sql tool call
        * Never modify data
        * Always fetch current values before explaining
        * If an updated or inserted identifier looks like a foreign key, you must query the referenced table row before giving the final answer
        * Do not stop at reporting raw ID values when a readable description can be fetched from the database
        * Resolve foreign keys by first checking the schema for related tables and column names, then query those tables
        * Do not use the updated base table as the source of the readable foreign-key meaning when a separate lookup table exists
        * Prefer human-readable columns only after confirming their exact names in the schema
        * If you need more schema detail, query INFORMATION_SCHEMA before querying business tables
        * Your final answer should mention both the raw ID change and the resolved readable value when available
        * For INSERT ... SELECT, explain what rows are being inserted, where they come from, what filters and joins are applied, and which computed expressions affect the inserted values

        Return a final natural language explanation.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly ISqlExecutorService _sqlExecutorService;
    private readonly ILogger<AiAgentService> _logger;
    private readonly GeminiOptions _options;

    public AiAgentService(
        HttpClient httpClient,
        ISqlExecutorService sqlExecutorService,
        IOptions<GeminiOptions> options,
        ILogger<AiAgentService> logger)
    {
        _httpClient = httpClient;
        _sqlExecutorService = sqlExecutorService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> AnalyzeQueryAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        }

        var parsedChange = SqlChangeParser.Parse(query);
        var primarySchemaQuery = BuildSchemaDiscoveryQuery(parsedChange.TargetObject);
        var primarySchema = string.IsNullOrWhiteSpace(primarySchemaQuery)
            ? "[]"
            : await _sqlExecutorService.ExecuteSelectAsJsonAsync(primarySchemaQuery, cancellationToken);
        var relatedSchemaQuery = BuildRelatedSchemaDiscoveryQuery(parsedChange);
        var relatedSchema = string.IsNullOrWhiteSpace(relatedSchemaQuery)
            ? "[]"
            : await _sqlExecutorService.ExecuteSelectAsJsonAsync(relatedSchemaQuery, cancellationToken);
        var contents = BuildConversation(parsedChange, primarySchema, relatedSchema);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var request = CreateGenerateContentRequest(contents);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini request failed with status {StatusCode}: {Body}", response.StatusCode, content);
                throw new InvalidOperationException(
                    $"Gemini request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response: {content}");
            }

            var assistantReply = ParseAssistantReply(content);
            contents.Add(assistantReply.ModelContent);

            if (assistantReply.ToolCalls.Count == 0)
            {
                var finalExplanation = assistantReply.Text;
                if (string.IsNullOrWhiteSpace(finalExplanation))
                {
                    throw new InvalidOperationException("The Gemini response did not include a final explanation.");
                }

                return SanitizeExplanation(finalExplanation);
            }

            foreach (var toolCall in assistantReply.ToolCalls)
            {
                var sql = toolCall.FunctionArguments["query"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Tool call did not include a query.");

                _logger.LogInformation("AI requested SQL lookup: {Sql}", sql);

                var jsonResult = await ExecuteToolQueryAsync(sql, cancellationToken);
                contents.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(
                        new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = toolCall.FunctionName,
                                ["response"] = new JsonObject
                                {
                                    ["result"] = jsonResult
                                }
                            }
                        })
                });
            }
        }

        throw new InvalidOperationException("The Gemini assistant did not finish within the allowed tool-call iterations.");
    }

    private async Task<JsonNode?> ExecuteToolQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var statements = SqlSafety.SplitSafeSelectStatements(sql);
        if (statements.Count == 1)
        {
            var singleResult = await _sqlExecutorService.ExecuteSelectAsJsonAsync(statements[0], cancellationToken);
            return JsonNode.Parse(singleResult);
        }

        var results = new JsonArray();
        foreach (var statement in statements)
        {
            var statementResult = await _sqlExecutorService.ExecuteSelectAsJsonAsync(statement, cancellationToken);
            results.Add(new JsonObject
            {
                ["query"] = statement,
                ["result"] = JsonNode.Parse(statementResult)
            });
        }

        return new JsonObject
        {
            ["statements"] = results
        };
    }

    private static string SanitizeExplanation(string explanation)
    {
        var normalized = explanation.Trim();

        normalized = Regex.Replace(normalized, @"\*\*(.*?)\*\*", "$1");
        normalized = Regex.Replace(normalized, @"__(.*?)__", "$1");
        normalized = Regex.Replace(normalized, @"(?<!\w)\*(?!\s)(.*?)(?<!\s)\*(?!\w)", "$1");
        normalized = Regex.Replace(normalized, @"(?<!\w)_(?!\s)(.*?)(?<!\s)_(?!\w)", "$1");
        normalized = Regex.Replace(normalized, @"`([^`]+)`", "$1");
        normalized = Regex.Replace(normalized, @"(?m)^\s*[-*+]\s+", string.Empty);
        normalized = Regex.Replace(normalized, @"[ \t]+\r?$", string.Empty, RegexOptions.Multiline);
        normalized = Regex.Replace(normalized, @"\r?\n{3,}", Environment.NewLine + Environment.NewLine);

        return normalized.Trim();
    }

    private List<JsonObject> BuildConversation(ParsedSqlChange parsedChange, string primarySchema, string relatedSchema)
    {
        var changeSummary = parsedChange.UpdatedColumns.Count == 0
            ? "(none parsed directly; inspect INSERT SELECT expressions and source data)"
            : string.Join(Environment.NewLine, parsedChange.UpdatedColumns.Select(column => $"- {column.ColumnName} = {column.RawValue}"));

        var parsingHint = $$"""
            Parsed change:
            Operation: {{parsedChange.Operation}}
            Target object: {{parsedChange.TargetObject ?? "(none detected)"}}
            Source objects:
            {{(parsedChange.SourceObjects.Count == 0 ? "- (none detected)" : string.Join(Environment.NewLine, parsedChange.SourceObjects.Select(table => $"- {table}")))}}
            Changed columns:
            {{changeSummary}}
            Predicate / filter:
            {{parsedChange.Predicate ?? "(none detected or not parsed)"}}

            Original SQL query:
            {{parsedChange.OriginalQuery}}
            """;

        var relationshipHints = BuildRelationshipHints(parsedChange);

        var targetSchemaHint = $$"""
            Initial schema snapshot for the primary target object from INFORMATION_SCHEMA.COLUMNS:
            {{primarySchema}}
            """;

        var relatedSchemaHint = $$"""
            Candidate related-table schema rows from INFORMATION_SCHEMA.COLUMNS:
            {{relatedSchema}}
            """;

        return
        [
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["text"] = $"{SystemPrompt}{Environment.NewLine}{Environment.NewLine}{targetSchemaHint}{Environment.NewLine}{Environment.NewLine}{relatedSchemaHint}{Environment.NewLine}{Environment.NewLine}{relationshipHints}{Environment.NewLine}{Environment.NewLine}{parsingHint}"
                    })
            }
        ];
    }

    private static string BuildRelationshipHints(ParsedSqlChange parsedChange)
    {
        var hints = parsedChange.UpdatedColumns
            .Select(column => column.ColumnName.Trim('[', ']'))
            .Where(columnName => columnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
            .Select(columnName => $"- {columnName} likely references table {columnName[..^2]} via key {columnName}")
            .ToList();

        if (hints.Count == 0)
        {
            return "Potential foreign-key hints: none inferred from updated column names.";
        }

        return "Potential foreign-key hints:" + Environment.NewLine + string.Join(Environment.NewLine, hints);
    }

    private static string BuildRelatedSchemaDiscoveryQuery(ParsedSqlChange parsedChange)
    {
        var objectNames = new List<string>();

        if (!string.IsNullOrWhiteSpace(parsedChange.TargetObject))
        {
            objectNames.Add(parsedChange.TargetObject);
        }

        objectNames.AddRange(parsedChange.SourceObjects);

        var relatedTableNames = parsedChange.UpdatedColumns
            .Select(column => column.ColumnName.Trim('[', ']'))
            .Where(columnName => columnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase) && columnName.Length > 2)
            .Select(columnName => columnName[..^2])
            .Concat(objectNames)
            .Select(NormalizeObjectName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("@", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (relatedTableNames.Count == 0)
        {
            return string.Empty;
        }

        var tableFilter = string.Join(", ", relatedTableNames.Select(name => $"'{EscapeSqlLiteral(name)}'"));
        var columnNames = parsedChange.UpdatedColumns
            .Select(column => column.ColumnName.Trim('[', ']'))
            .Where(columnName => columnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var whereClauses = new List<string>
        {
            $"TABLE_NAME IN ({tableFilter})"
        };

        if (columnNames.Count > 0)
        {
            var columnFilter = string.Join(", ", columnNames.Select(name => $"'{EscapeSqlLiteral(name)}'"));
            whereClauses.Add($"COLUMN_NAME IN ({columnFilter})");
        }

        return $$"""
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME,
                DATA_TYPE,
                ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE {{string.Join(Environment.NewLine + "   OR ", whereClauses)}}
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
            """;
    }

    private static string BuildSchemaDiscoveryQuery(string? rawObjectName)
    {
        var normalized = NormalizeObjectName(rawObjectName);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("@", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return $$"""
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME,
                DATA_TYPE,
                ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{{EscapeSqlLiteral(normalized)}}'
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
            """;
    }

    private static string NormalizeObjectName(string? rawObjectName)
    {
        if (string.IsNullOrWhiteSpace(rawObjectName))
        {
            return string.Empty;
        }

        var normalized = rawObjectName.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private HttpRequestMessage CreateGenerateContentRequest(List<JsonObject> contents)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey!)}");

        var payload = new JsonObject
        {
            ["contents"] = new JsonArray(contents.Select(content => content.DeepClone()).ToArray()),
            ["tools"] = new JsonArray(
                new JsonObject
                {
                    ["functionDeclarations"] = new JsonArray(
                        new JsonObject
                        {
                            ["name"] = "run_sql",
                            ["description"] = "Run a safe SELECT query against the SQL Server database for schema discovery or data inspection and return JSON rows.",
                            ["parameters"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["query"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["description"] = "A SQL SELECT query used to inspect schema or data related to the SQL change statement."
                                    }
                                },
                                ["required"] = new JsonArray("query")
                            }
                        })
                }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.2
            }
        };

        var body = payload.ToJsonString(JsonOptions);
        _logger.LogInformation("Sending Gemini request with {ContentCount} contents.", contents.Count);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private static AssistantReply ParseAssistantReply(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("candidates", out var candidatesElement) ||
            candidatesElement.ValueKind != JsonValueKind.Array ||
            candidatesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"The Gemini response did not include any candidates. Response: {responseBody}");
        }

        var candidate = candidatesElement[0].GetProperty("content");

        var toolCalls = new List<ToolCall>();
        var textParts = new List<string>();

        if (candidate.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in partsElement.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                {
                    textParts.Add(textElement.GetString() ?? string.Empty);
                }

                if (part.TryGetProperty("functionCall", out var functionCallElement))
                {
                    var functionArguments = functionCallElement.TryGetProperty("args", out var argsElement)
                        ? JsonNode.Parse(argsElement.GetRawText())?.AsObject() ?? []
                        : [];

                    toolCalls.Add(new ToolCall(
                        functionCallElement.GetProperty("name").GetString() ?? string.Empty,
                        functionArguments));
                }
            }
        }

        var modelContent = JsonNode.Parse(candidate.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("The Gemini response did not include content.");

        return new AssistantReply(modelContent, string.Join(Environment.NewLine, textParts), toolCalls);
    }

    private sealed record AssistantReply(JsonObject ModelContent, string Text, List<ToolCall> ToolCalls);

    private sealed record ToolCall(string FunctionName, JsonObject FunctionArguments);
}
