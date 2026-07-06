using Microsoft.AspNetCore.Mvc;
using QueryAssist.Models;
using QueryAssist.Services;

namespace QueryAssist.Controllers;

[ApiController]
[Route("")]
public sealed class QueryController : ControllerBase
{
    private readonly IAiAgentService _aiAgentService;
    private readonly ILogger<QueryController> _logger;

    public QueryController(
        IAiAgentService aiAgentService,
        ILogger<QueryController> logger)
    {
        _aiAgentService = aiAgentService;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Index()
    {
        return Ok(new
        {
            message = "QueryAssist is running.",
            endpoints = new[]
            {
                "POST /analyze-query",
                "POST /generate-query",
                "POST /ask",
                "POST /queue-sps",
                "POST /explain-sp",
                "POST /queue-tables",
                "POST /queue-functions",
                "POST /explain-table",
                "POST /route-question"
            },
            exampleBody = new
            {
                query = "UPDATE Application SET ApplicationTypeID = 2 WHERE ApplicationID = 123"
            },
            generateExampleBody = new
            {
                requirement = "What is the query to change the application type to planning permit?"
            },
            askExampleBody = new
            {
                question = "How many applications are there by type?"
            }
        });
    }

    [HttpPost("analyze-query")]
    [Produces("text/plain", "application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(AnalyzeQueryResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> AnalyzeQuery(
        [FromBody] AnalyzeQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["query"] = ["The query field is required."]
            }));
        }

        _logger.LogInformation("Received analyze-query request for SQL: {Sql}", request.Query);

        var explanation = await _aiAgentService.AnalyzeQueryAsync(request.Query, cancellationToken);

        var acceptsJson = Request.GetTypedHeaders().Accept?.Any(header =>
            header.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) == true;

        if (acceptsJson)
        {
            return Ok(new AnalyzeQueryResponse(explanation));
        }

        return Content(explanation, "text/plain");
    }

    [HttpPost("generate-query")]
    [Produces("text/plain", "application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(GenerateQueryResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> GenerateQuery(
        [FromBody] GenerateQueryRequest request,
        CancellationToken cancellationToken)
    {
        var requirement = request.GetRequirement();
        if (string.IsNullOrWhiteSpace(requirement))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["requirement"] = ["Provide one of: requirement, requirements, or query."]
            }));
        }

        _logger.LogInformation("Received generate-query request.");

        var generatedQuery = await _aiAgentService.GenerateQueryAsync(requirement, cancellationToken);

        var acceptsJson = Request.GetTypedHeaders().Accept?.Any(header =>
            header.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) == true;

        if (acceptsJson)
        {
            return Ok(new GenerateQueryResponse(generatedQuery));
        }

        return Content(generatedQuery, "text/plain");
    }

    [HttpPost("ask")]
    [ProducesResponseType(typeof(AskOrchestratorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> AskQuestion(
        [FromBody] AskQuestionRequest request,
        [FromServices] IAskOrchestratorService orchestrator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["question"] = ["The question field is required."]
            }));
        }

        _logger.LogInformation("Received orchestrated ask request.");

        var response = await orchestrator.AskAsync(request.Question, cancellationToken);
        
        return Ok(response);
    }

    [HttpPost("queue-sps")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult QueueStoredProcedures(
        [FromBody] BulkEmbedSpRequest request,
        [FromServices] SpEmbeddingQueue queue)
    {
        var count = 0;

        if (request?.StoredProcedures != null && request.StoredProcedures.Count > 0)
        {
            foreach (var sp in request.StoredProcedures)
            {
                if (!string.IsNullOrWhiteSpace(sp.Name) && !string.IsNullOrWhiteSpace(sp.Text))
                {
                    queue.QueueBackgroundWorkItem(sp);
                    count++;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(request?.RawExportData))
        {
            // Split by newline where the next line starts with a number (object_id) and a tab
            var rows = System.Text.RegularExpressions.Regex.Split(request.RawExportData, @"(?:\r?\n)(?=\d+\t)");
            
            foreach (var row in rows)
            {
                // Pattern: object_id \t schema \t name \t definition \t date \t date
                var match = System.Text.RegularExpressions.Regex.Match(row.Trim(), @"^\d+\t[^\t]+\t([^\t]+)\t([\s\S]+?)\t\d{4}-\d{2}-\d{2}.*?\t\d{4}-\d{2}-\d{2}");
                if (match.Success)
                {
                    var spName = match.Groups[1].Value.Trim();
                    var spText = match.Groups[2].Value.Trim();
                    
                    if (!string.IsNullOrWhiteSpace(spName) && !string.IsNullOrWhiteSpace(spText))
                    {
                        queue.QueueBackgroundWorkItem(new SpItem { Name = spName, Text = spText });
                        count++;
                    }
                }
            }
        }

        if (count == 0)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["payload"] = ["No valid stored procedures were found in the provided payload."]
            }));
        }

        _logger.LogInformation("Queued {Count} SPs for embedding generation.", count);

        return Accepted(new { message = $"Successfully queued {count} stored procedures for background processing." });
    }

    [HttpPost("queue-tables")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult QueueTables(
        [FromBody] BulkEmbedTableRequest request,
        [FromServices] TableEmbeddingQueue queue)
    {
        var count = 0;

        if (request?.Tables != null && request.Tables.Count > 0)
        {
            foreach (var table in request.Tables)
            {
                if (!string.IsNullOrWhiteSpace(table.Name) && table.Columns.Count > 0)
                {
                    queue.QueueBackgroundWorkItem(table);
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["payload"] = ["No valid tables were found in the provided payload."]
            }));
        }

        _logger.LogInformation("Queued {Count} Tables for embedding generation.", count);

        return Accepted(new { message = $"Successfully queued {count} tables for background processing." });
    }

    [HttpPost("queue-functions")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult QueueFunctions(
        [FromBody] BulkEmbedFunctionRequest request,
        [FromServices] FunctionEmbeddingQueue queue)
    {
        var count = 0;

        if (request?.Functions != null && request.Functions.Count > 0)
        {
            foreach (var func in request.Functions)
            {
                if (!string.IsNullOrWhiteSpace(func.Name) && !string.IsNullOrWhiteSpace(func.Definition))
                {
                    queue.QueueBackgroundWorkItem(func);
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["payload"] = ["No valid functions were found in the provided payload."]
            }));
        }

        _logger.LogInformation("Queued {Count} Functions for embedding generation.", count);

        return Accepted(new { message = $"Successfully queued {count} functions for background processing." });
    }

    [HttpPost("queue-relationships")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult QueueRelationships(
        [FromBody] BulkEmbedRelationshipRequest request,
        [FromServices] RelationshipEmbeddingQueue queue)
    {
        var count = 0;

        if (request?.Relationships != null && request.Relationships.Count > 0)
        {
            foreach (var rel in request.Relationships)
            {
                if (!string.IsNullOrWhiteSpace(rel.ForeignKey))
                {
                    queue.QueueBackgroundWorkItem(rel);
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["payload"] = ["No valid relationships were found in the provided payload."]
            }));
        }

        _logger.LogInformation("Queued {Count} Relationships for embedding generation.", count);

        return Accepted(new { message = $"Successfully queued {count} relationships for background processing." });
    }

    [HttpPost("explain-sp")]
    [ProducesResponseType(typeof(ExplainSpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExplainStoredProcedure(
        [FromBody] ExplainSpRequest request,
        [FromServices] IEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["question"] = ["The question field is required."]
            }));
        }

        var filePath = "StoredProcedures.json";
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Stored procedures cache not found. Please queue SPs first." });
        }

        var json = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
        var sps = System.Text.Json.JsonSerializer.Deserialize<List<StoredProcedureEmbedding>>(json) ?? new List<StoredProcedureEmbedding>();

        if (sps.Count == 0)
        {
            return NotFound(new { message = "Stored procedures cache is empty. Please queue SPs first." });
        }

        // Generate embedding for the question
        var questionEmbedding = await embeddingService.GenerateEmbeddingAsync(request.Question, cancellationToken);

        // Find the SP with the highest cosine similarity
        StoredProcedureEmbedding? bestSp = null;
        float bestSimilarity = -2.0f;

        foreach (var sp in sps)
        {
            if (sp.Embedding == null || sp.Embedding.Length != questionEmbedding.Length)
            {
                continue;
            }

            float similarity = embeddingService.CosineSimilarity(questionEmbedding, sp.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestSp = sp;
            }
        }

        if (bestSp == null || string.IsNullOrWhiteSpace(bestSp.Text))
        {
            return NotFound(new { message = "Could not find a matching stored procedure for the given question." });
        }

        _logger.LogInformation("Explaining SP: {SpName} (Similarity: {Score:F3}) for question: {Question}", bestSp.Name, bestSimilarity, request.Question);

        var explanation = await _aiAgentService.ExplainStoredProcedureAsync(bestSp.Name, bestSp.Text, cancellationToken);

        return Ok(new ExplainSpResponse(bestSp.Name, bestSimilarity, explanation));
    }

    [HttpPost("explain-table")]
    [ProducesResponseType(typeof(ExplainTableResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExplainTable(
        [FromBody] ExplainTableRequest request,
        [FromServices] IEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["question"] = ["The question field is required."]
            }));
        }

        var filePath = "Tables.json";
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Tables cache not found. Please queue tables first." });
        }

        var json = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
        var tables = System.Text.Json.JsonSerializer.Deserialize<List<TableEmbedding>>(json) ?? new List<TableEmbedding>();

        if (tables.Count == 0)
        {
            return NotFound(new { message = "Tables cache is empty. Please queue tables first." });
        }

        // Generate embedding for the question
        var questionEmbedding = await embeddingService.GenerateEmbeddingAsync(request.Question, cancellationToken);

        // Find the Table with the highest cosine similarity
        TableEmbedding? bestTable = null;
        float bestSimilarity = -2.0f;

        foreach (var table in tables)
        {
            if (table.Embedding == null || table.Embedding.Length != questionEmbedding.Length)
            {
                continue;
            }

            float similarity = embeddingService.CosineSimilarity(questionEmbedding, table.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestTable = table;
            }
        }

        if (bestTable == null || string.IsNullOrWhiteSpace(bestTable.Text))
        {
            return NotFound(new { message = "Could not find a matching table for the given question." });
        }

        _logger.LogInformation("Explaining Table: {TableName} (Similarity: {Score:F3}) for question: {Question}", bestTable.Name, bestSimilarity, request.Question);

        var explanation = await _aiAgentService.ExplainTableAsync(bestTable.Name, bestTable.Text, cancellationToken);

        return Ok(new ExplainTableResponse(bestTable.Name, bestSimilarity, explanation));
    }

    [HttpPost("route-question")]
    [ProducesResponseType(typeof(RouteQuestionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RouteQuestion(
        [FromBody] RouteQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["question"] = ["The question field is required."]
            }));
        }

        _logger.LogInformation("Routing question: {Question}", request.Question);

        var collections = await _aiAgentService.DetermineKnowledgeSourcesAsync(request.Question, cancellationToken);

        return Ok(new RouteQuestionResponse { Collections = collections });
    }
}
