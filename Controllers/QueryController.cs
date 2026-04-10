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

    public QueryController(IAiAgentService aiAgentService, ILogger<QueryController> logger)
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
            endpoint = "POST /analyze-query",
            exampleBody = new
            {
                query = "UPDATE Application SET ApplicationTypeID = 2 WHERE ApplicationID = 123"
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
}
