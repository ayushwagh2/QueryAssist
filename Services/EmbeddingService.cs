using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using QueryAssist.Options;

namespace QueryAssist.Services;

public sealed class EmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _geminiOptions;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _geminiOptions = geminiOptions.Value;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync([text], cancellationToken);
        return embeddings[0];
    }

    public async Task<float[][]> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("Generating embeddings for {Count} texts in parallel (max 5 concurrent)...", texts.Count);

        // Process embeddings in parallel with a semaphore to limit concurrency
        var semaphore = new SemaphoreSlim(5, 5);
        var tasks = texts.Select(async (text, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (index % 10 == 0)
                {
                    _logger.LogInformation("Progress: {Current}/{Total} embeddings generated", index, texts.Count);
                }
                return await GenerateEmbeddingInternalAsync(text, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var embeddings = await Task.WhenAll(tasks);
        
        _logger.LogInformation("Completed generating {Count} embeddings", embeddings.Length);
        
        return embeddings;
    }

    private async Task<float[]> GenerateEmbeddingInternalAsync(string text, CancellationToken cancellationToken)
    {
        // Use the correct endpoint format for Gemini embedding API
        var url = $"{_geminiOptions.BaseUrl.TrimEnd('/')}/models/{_embeddingOptions.Model}:embedContent?key={Uri.EscapeDataString(_geminiOptions.ApiKey!)}";

        var payload = new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Embedding request failed: {StatusCode} - {Body}", response.StatusCode, content);
            throw new InvalidOperationException($"Embedding request failed: {response.StatusCode}. Response: {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var embedding = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values")
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();

        return embedding;
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length.");
        }

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }
}