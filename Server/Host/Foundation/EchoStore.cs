using System.Text;
using AstralOath.Protocol;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace AstralOath.Server.Foundation;

internal sealed class EchoStore(string? connectionString)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(connectionString);

    private MySqlConnection CreateConnection() => new(connectionString);

    public async Task<ulong?> ResolveAsync(EchoRequest request, CancellationToken cancellationToken)
    {
        var requestId = Encoding.ASCII.GetBytes(request.RequestId);
        var message = Encoding.UTF8.GetBytes(request.Message);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // The unique index serializes concurrent retries. The no-op update keeps
        // the committed payload intact and holds its row lock until commit.
        using var insert = new MySqlCommand("""
            INSERT INTO foundation_echo (request_id, protocol_version, message)
            VALUES (@id, @version, @message)
            ON DUPLICATE KEY UPDATE sequence = sequence;
            """, connection, transaction);
        insert.Parameters.AddWithValue("@id", requestId);
        insert.Parameters.AddWithValue("@version", request.ProtocolVersion);
        insert.Parameters.AddWithValue("@message", message);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        using var read = new MySqlCommand("""
            SELECT sequence, protocol_version, message
            FROM foundation_echo WHERE request_id = @id FOR UPDATE;
            """, connection, transaction);
        read.Parameters.AddWithValue("@id", requestId);
        ulong sequence;
        bool matches;
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The echo row disappeared inside its transaction.");
            sequence = reader.GetUInt64(0);
            matches = reader.GetUInt32(1) == request.ProtocolVersion
                && message.AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(2));
        }

        if (!matches)
            return null;

        await transaction.CommitAsync(cancellationToken);
        return sequence;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var read = new MySqlCommand("SELECT sequence FROM foundation_echo LIMIT 1;", connection);
        await read.ExecuteScalarAsync(cancellationToken);
    }
}

internal sealed class DatabaseHealthCheck(EchoStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!store.IsConfigured)
            return HealthCheckResult.Unhealthy("MySQL is unconfigured.");

        await store.CheckAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
