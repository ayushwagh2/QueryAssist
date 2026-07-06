using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QueryAssist.Models;

namespace QueryAssist.Services.Tools;

public class ExplainFunctionTool : IAiTool
{
    public string Name => "ExplainFunction";
    public string Description => "Search for a database Function based on a natural language question and return its definition. Use this when the user asks about specific functions.";

    public JsonObject Parameters => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["question"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's question about the function."
            }
        },
        ["required"] = new JsonArray("question")
    };

    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ExplainFunctionTool> _logger;

    public ExplainFunctionTool(
        IEmbeddingService embeddingService,
        ILogger<ExplainFunctionTool> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var question = arguments["question"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Error: Missing required argument 'question'.";
        }

        var filePath = "Functions.json";
        if (!File.Exists(filePath))
        {
            return "Error: Functions cache not found. Please queue functions first.";
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var functions = JsonSerializer.Deserialize<List<FunctionEmbedding>>(json) ?? new List<FunctionEmbedding>();

        if (functions.Count == 0)
        {
            return "Error: Functions cache is empty. Please queue functions first.";
        }

        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        FunctionEmbedding? bestFunc = null;
        float bestSimilarity = -2.0f;

        foreach (var func in functions)
        {
            if (func.Embedding == null || func.Embedding.Length != questionEmbedding.Length) continue;

            float similarity = _embeddingService.CosineSimilarity(questionEmbedding, func.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestFunc = func;
            }
        }

        if (bestFunc == null || string.IsNullOrWhiteSpace(bestFunc.Text))
        {
            return "Could not find a matching function for the given question.";
        }

        _logger.LogInformation("Tool ExplainFunction selected {FuncName} with similarity {Score:F3}", bestFunc.Name, bestSimilarity);

        // Returning the raw text so the Orchestrator LLM can read it directly
        return $"Function: {bestFunc.Name}\nDefinition: {bestFunc.Text}";
    }
}
