using System.Text.RegularExpressions;
using QueryAssist.Models;

namespace QueryAssist.Utilities;

public static partial class SqlChangeParser
{
    public static ParsedSqlChange Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("The SQL query cannot be empty.");
        }

        var trimmed = sql.Trim();
        if (trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUpdate(trimmed);
        }

        if (trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            return ParseInsert(trimmed);
        }

        throw new InvalidOperationException("Only UPDATE and INSERT statements are supported right now.");
    }

    private static ParsedSqlChange ParseUpdate(string sql)
    {
        var match = UpdateRegex().Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException("Only simple UPDATE statements are supported.");
        }

        var targetObject = match.Groups["table"].Value.Trim();
        var setClause = match.Groups["set"].Value.Trim();
        var whereClause = match.Groups["where"].Success ? match.Groups["where"].Value.Trim() : null;

        var updates = setClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseAssignment)
            .ToList();

        return new ParsedSqlChange(
            "UPDATE",
            targetObject,
            ExtractTableReferences(sql),
            updates,
            whereClause,
            sql);
    }

    private static ParsedSqlChange ParseInsert(string sql)
    {
        var match = InsertRegex().Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException("Only INSERT INTO statements are supported.");
        }

        var targetObject = match.Groups["target"].Value.Trim();

        return new ParsedSqlChange(
            "INSERT",
            targetObject,
            ExtractTableReferences(sql),
            [],
            null,
            sql);
    }

    private static List<string> ExtractTableReferences(string sql)
    {
        return TableReferenceRegex()
            .Matches(sql)
            .Select(match => match.Groups["table"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static UpdatedColumn ParseAssignment(string assignment)
    {
        var parts = assignment.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid SET assignment: {assignment}");
        }

        return new UpdatedColumn(parts[0], parts[1]);
    }

    [GeneratedRegex(
        @"^UPDATE\s+(?<table>[@\[\]\w\.]+)\s+SET\s+(?<set>.+?)(?:\s+WHERE\s+(?<where>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UpdateRegex();

    [GeneratedRegex(
        @"^INSERT\s+INTO\s+(?<target>[@\[\]\w\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InsertRegex();

    [GeneratedRegex(
        @"\b(?:FROM|JOIN)\s+(?<table>@?[\[\]\w\.]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TableReferenceRegex();
}
