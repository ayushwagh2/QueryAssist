namespace QueryAssist.Models;

public class AnalyzeSpResponse
{
    public string Analysis { get; set; } = string.Empty;
    public List<string> ExtractedTables { get; set; } = new();
    public List<string> ExtractedFilters { get; set; } = new();
}
