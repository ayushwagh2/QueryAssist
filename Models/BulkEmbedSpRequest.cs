namespace QueryAssist.Models;

public class BulkEmbedSpRequest
{
    public List<SpItem> StoredProcedures { get; set; } = new();
    public string RawExportData { get; set; } = string.Empty;
}

public class SpItem
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
