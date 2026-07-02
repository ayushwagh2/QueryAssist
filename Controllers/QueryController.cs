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

    [HttpGet]
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
                "POST /explain-sp"
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
    [Produces("text/plain", "application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(AskQuestionResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> AskQuestion(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["question"] = ["The question field is required."]
            }));
        }

        _logger.LogInformation("Received ask request.");

        var answer = await _aiAgentService.AskQuestionAsync(request.Question, cancellationToken);

        var acceptsJson = Request.GetTypedHeaders().Accept?.Any(header =>
            header.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) == true;

        if (acceptsJson)
        {
            return Ok(new AskQuestionResponse(answer));
        }

        return Content(answer, "text/plain");
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
}
