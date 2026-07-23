using System.Text.RegularExpressions;

namespace QueryAssist.Utilities;

public static partial class SpParser
{
    public static (List<string> Tables, List<string> Filters) Parse(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sql))
        {
            return (tables.ToList(), filters.ToList());
        }

        // Extract tables from FROM and JOIN
        var tableMatches = TableReferenceRegex().Matches(sql);
        foreach (Match match in tableMatches)
        {
            if (match.Groups["table"].Success)
            {
                tables.Add(match.Groups["table"].Value.Trim());
            }
        }

        // Extract filters from WHERE clauses
        var whereMatches = WhereClauseRegex().Matches(sql);
        foreach (Match match in whereMatches)
        {
            if (match.Groups["filter"].Success)
            {
                filters.Add(match.Groups["filter"].Value.Trim());
            }
        }

        return (tables.ToList(), filters.ToList());
    }

    [GeneratedRegex(
        @"\b(?:FROM|JOIN)\s+(?<table>@?[\[\]\w\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TableReferenceRegex();

    [GeneratedRegex(
        @"\bWHERE\s+(?<filter>.+?)(?=\b(?:GROUP\s+BY|ORDER\s+BY|HAVING|OPTION|FOR|UNION|INTERSECT|EXCEPT|;|--|\/\*)\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereClauseRegex();
}
