namespace QueryAssist.Models;

public sealed record ParsedSqlChange(
    string Operation,
    string? TargetObject,
    IReadOnlyList<string> SourceObjects,
    IReadOnlyList<UpdatedColumn> UpdatedColumns,
    string? Predicate,
    string OriginalQuery);

public sealed record UpdatedColumn(string ColumnName, string RawValue);
