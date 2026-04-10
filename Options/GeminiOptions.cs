namespace QueryAssist.Options;

public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash-lite";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}
