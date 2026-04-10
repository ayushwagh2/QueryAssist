using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using QueryAssist.Utilities;

namespace QueryAssist.Services;

public sealed class SqlExecutorService : ISqlExecutorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ILogger<SqlExecutorService> _logger;
    private readonly IConfiguration _configuration;

    public SqlExecutorService(ILogger<SqlExecutorService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string> ExecuteSelectAsJsonAsync(string query, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("GreenlightV4Entities");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:GreenlightV4Entities is not configured.");
        }

        connectionString = NormalizeConnectionString(connectionString);

        var safeQuery = SqlSafety.EnsureSafeSelect(query);
        _logger.LogInformation("Executing safe SELECT query: {Sql}", safeQuery);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            safeQuery,
            commandType: CommandType.Text,
            commandTimeout: 10,
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync(command))
            .Select(row => (IDictionary<string, object?>)row)
            .Select(row => row.ToDictionary(
                entry => entry.Key,
                entry => NormalizeValue(entry.Value)))
            .ToList();

        return JsonSerializer.Serialize(rows, JsonOptions);
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        if (builder.TryGetValue("provider connection string", out var providerConnectionString) &&
            providerConnectionString is string sqlConnectionString &&
            !string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            return sqlConnectionString;
        }

        return connectionString;
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null or DBNull => null,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }
}
