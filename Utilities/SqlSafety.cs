using System.Text.RegularExpressions;

namespace QueryAssist.Utilities;

public static partial class SqlSafety
{
    private static readonly string[] ForbiddenTokens =
    [
        "UPDATE",
        "DELETE",
        "INSERT",
        "DROP",
        "ALTER",
        "TRUNCATE",
        "MERGE",
        "EXEC",
        "CREATE"
    ];

    public static string EnsureSafeSelect(string query)
    {
        var statements = SplitStatements(query);
        if (statements.Count != 1)
        {
            throw new InvalidOperationException("Only a single SQL statement is allowed.");
        }

        return EnsureSafeSingleSelect(statements[0]);
    }

    public static IReadOnlyList<string> SplitSafeSelectStatements(string query)
    {
        var statements = SplitStatements(query);
        if (statements.Count == 0)
        {
            throw new InvalidOperationException("SQL query cannot be empty.");
        }

        return statements
            .Select(EnsureSafeSingleSelect)
            .ToArray();
    }

    private static string NormalizeQuery(string query)
    {
        var trimmed = query.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .ToList();

            if (lines.Count >= 2 && lines[0].StartsWith("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(0);

                if (lines.Count > 0 && lines[^1].Trim().Equals("```", StringComparison.Ordinal))
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                trimmed = string.Join(Environment.NewLine, lines).Trim();
            }
        }

        return trimmed.TrimEnd().TrimEnd(';').TrimEnd();
    }

    private static List<string> SplitStatements(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("SQL query cannot be empty.");
        }

        var normalized = NormalizeQuery(query);
        var statements = new List<string>();
        var current = new List<char>();
        var inSingleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < normalized.Length; index++)
        {
            var currentChar = normalized[index];
            var next = index + 1 < normalized.Length ? normalized[index + 1] : '\0';

            current.Add(currentChar);

            if (inLineComment)
            {
                if (currentChar is '\r' or '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (currentChar == '*' && next == '/')
                {
                    current.Add(next);
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (currentChar == '\'')
                {
                    if (next == '\'')
                    {
                        current.Add(next);
                        index++;
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (currentChar == '-' && next == '-')
            {
                current.Add(next);
                inLineComment = true;
                index++;
                continue;
            }

            if (currentChar == '/' && next == '*')
            {
                current.Add(next);
                inBlockComment = true;
                index++;
                continue;
            }

            if (currentChar == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (currentChar == ';')
            {
                var statementText = new string(current.ToArray()).Trim();
                if (statementText.Length > 0)
                {
                    var withoutSemicolon = statementText[..^1].TrimEnd();
                    if (withoutSemicolon.Length > 0)
                    {
                        statements.Add(withoutSemicolon);
                    }
                }

                current.Clear();
            }
        }

        var trailingStatement = new string(current.ToArray()).Trim();
        if (trailingStatement.Length > 0)
        {
            statements.Add(trailingStatement);
        }

        return statements
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToList();
    }

    private static string EnsureSafeSingleSelect(string query)
    {
        var trimmed = query.Trim();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only SELECT statements are allowed.");
        }

        if (ContainsStatementSeparator(trimmed))
        {
            throw new InvalidOperationException("Only a single SQL statement is allowed.");
        }

        var uppercase = trimmed.ToUpperInvariant();
        foreach (var token in ForbiddenTokens)
        {
            if (Regex.IsMatch(uppercase, $@"\b{token}\b"))
            {
                throw new InvalidOperationException($"Unsafe SQL token detected: {token}.");
            }
        }

        if (uppercase.Contains("INFORMATION_SCHEMA.COLUMNS", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (!TopRegex().IsMatch(trimmed))
        {
            trimmed = SelectRegex().Replace(trimmed, "SELECT TOP 50 ", 1);
        }

        return trimmed;
    }

    private static bool ContainsStatementSeparator(string query)
    {
        var inSingleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < query.Length; index++)
        {
            var current = query[index];
            var next = index + 1 < query.Length ? query[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (current == '\'')
                {
                    if (next == '\'')
                    {
                        index++;
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                inLineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (current == ';')
            {
                return !HasOnlyTrailingTrivia(query, index + 1);
            }
        }

        return false;
    }

    private static bool HasOnlyTrailingTrivia(string query, int startIndex)
    {
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = startIndex; index < query.Length; index++)
        {
            var current = query[index];
            var next = index + 1 < query.Length ? query[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            if (current == '-' && next == '-')
            {
                inLineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            return false;
        }

        return true;
    }

    [GeneratedRegex(@"^\s*SELECT\s+TOP\s+\(?\d+\)?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex TopRegex();

    [GeneratedRegex(@"^\s*SELECT\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SelectRegex();
}
