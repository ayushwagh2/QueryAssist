using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueryAssist.Models;
using QueryAssist.Options;
using QueryAssist.Services.Tools;

namespace QueryAssist.Services;

public interface IAskOrchestratorService
{
    Task<AskOrchestratorResponse> AskAsync(string question, CancellationToken cancellationToken = default);
}

public class AskOrchestratorService : IAskOrchestratorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private const string SystemPrompt = """
        You are a highly intelligent SQL Database Assistant. 
        Your job is to answer user questions about the database.
        
        You have a set of tools available to you. 
        Analyze the user's question and decide which tool(s) to call to gather the necessary context.
        You can call multiple tools if needed.
        
        Once you receive the results from the tools, synthesize the information into a clear, helpful, and user-friendly answer.
        """;

    private readonly HttpClient _httpClient;
    private readonly IEnumerable<IAiTool> _tools;
    private readonly GeminiOptions _options;
    private readonly ILogger<AskOrchestratorService> _logger;

    public AskOrchestratorService(
        HttpClient httpClient,
        IEnumerable<IAiTool> tools,
        IOptions<GeminiOptions> options,
        ILogger<AskOrchestratorService> logger)
    {
        _httpClient = httpClient;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AskOrchestratorResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        }

        _logger.LogInformation("Orchestrator received question: {Question}", question);

        var contents = new List<JsonObject>
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["text"] = $"{SystemPrompt}\n\nUser Question:\n{question}"
                    })
            }
        };

        // First LLM Call: Determine tools to execute
        var request = CreateGenerateContentRequest(contents);
        var assistantReply = await SendGeminiRequestAsync(request, cancellationToken);
        contents.Add(assistantReply.ModelContent);

        var response = new AskOrchestratorResponse { Question = question };

        if (assistantReply.ToolCalls.Count == 0)
        {
            // The LLM decided it doesn't need any tools and can answer directly
            _logger.LogInformation("Orchestrator decided no tools were needed.");
            response.Answer = assistantReply.Text;
            return response;
        }

        // Execute Tools concurrently
        var executionTasks = assistantReply.ToolCalls.Select(async toolCall =>
        {
            var tool = _tools.FirstOrDefault(t => t.Name.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                _logger.LogWarning("LLM requested unknown tool: {ToolName}", toolCall.FunctionName);
                return new ToolExecutionData
                {
                    Name = toolCall.FunctionName,
                    Arguments = toolCall.FunctionArguments,
                    Result = "Error: Tool not found."
                };
            }

            try
            {
                var result = await tool.ExecuteAsync(toolCall.FunctionArguments, cancellationToken);
                return new ToolExecutionData
                {
                    Name = toolCall.FunctionName,
                    Arguments = toolCall.FunctionArguments,
                    Result = result
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool {ToolName}", toolCall.FunctionName);
                return new ToolExecutionData
                {
                    Name = toolCall.FunctionName,
                    Arguments = toolCall.FunctionArguments,
                    Result = $"Error: {ex.Message}"
                };
            }
        }).ToList();

        var toolResults = await Task.WhenAll(executionTasks);
        response.ToolsExecuted.AddRange(toolResults);

        // Append tool results to conversation
        var functionResponses = new JsonArray();
        foreach (var toolResult in toolResults)
        {
            functionResponses.Add(new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = toolResult.Name,
                    ["response"] = new JsonObject
                    {
                        ["result"] = toolResult.Result
                    }
                }
            });
        }

        contents.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = functionResponses
        });

        // Second LLM Call: Generate final answer
        var finalRequest = CreateGenerateContentRequest(contents);
        var finalReply = await SendGeminiRequestAsync(finalRequest, cancellationToken);

        response.Answer = finalReply.Text;

        return response;
    }

    private HttpRequestMessage CreateGenerateContentRequest(List<JsonObject> contents)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey!)}");

        var functionDeclarations = new JsonArray();
        foreach (var tool in _tools)
        {
            functionDeclarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters.DeepClone()
            });
        }

        var payload = new JsonObject
        {
            ["contents"] = new JsonArray(contents.Select(content => content.DeepClone()).ToArray()),
            ["tools"] = new JsonArray(
                new JsonObject
                {
                    ["functionDeclarations"] = functionDeclarations
                }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.2
            }
        };

        request.Content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<AssistantReply> SendGeminiRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini request failed with status {StatusCode}: {Body}", response.StatusCode, content);
            throw new InvalidOperationException(
                $"Gemini request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response: {content}");
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("candidates", out var candidatesElement) ||
            candidatesElement.ValueKind != JsonValueKind.Array ||
            candidatesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"The Gemini response did not include any candidates. Response: {content}");
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
