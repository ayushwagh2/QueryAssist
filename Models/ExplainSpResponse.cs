namespace QueryAssist.Models;

public class ExplainSpResponse
{
    public string SpName { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public ExplainSpResponse() { }

    public ExplainSpResponse(string spName, double similarityScore, string explanation)
    {
        SpName = spName;
        SimilarityScore = Math.Round(similarityScore, 3);
        Explanation = explanation;
    }
}
