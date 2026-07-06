using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace QueryAssist.Services.Tools;

public class GenerateSqlTool : IAiTool
{
    public string Name => "GenerateSQL";
    public string Description => "Generate a SQL query based on a natural language requirement. Use this when the user asks 'how do I query X' or wants a script to run.";

    public JsonObject Parameters => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["requirement"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's requirement for the SQL query."
            }
        },
        ["required"] = new JsonArray("requirement")
    };

    private readonly IAiAgentService _aiAgentService;
    private readonly ILogger<GenerateSqlTool> _logger;

    public GenerateSqlTool(
        IAiAgentService aiAgentService,
        ILogger<GenerateSqlTool> logger)
    {
        _aiAgentService = aiAgentService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var requirement = arguments["requirement"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(requirement))
        {
            return "Error: Missing required argument 'requirement'.";
        }

        _logger.LogInformation("Tool GenerateSQL generating query for: {Requirement}", requirement);

        var generatedSql = await _aiAgentService.GenerateQueryAsync(requirement, cancellationToken);
        
        return generatedSql;
    }
}
