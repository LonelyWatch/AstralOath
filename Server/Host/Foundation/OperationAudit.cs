using MySqlConnector;

namespace AstralOath.Server.Foundation;

internal static class OperationAudit
{
    // Authoritative operations pass the same transaction as the business write.
    internal static async Task Record(MySqlConnection connection, MySqlTransaction? transaction, string operation,
        string actorType, string actorId, string? subjectType, string? subjectId, string? clientEventId,
        CancellationToken token)
    {
        using var command = new MySqlCommand("""
            INSERT INTO game_operation_audit
                (operation, actor_type, actor_id, subject_type, subject_id, client_event_id, occurred_at)
            VALUES (@operation, @actorType, @actorId, @subjectType, @subjectId, @event, UTC_TIMESTAMP(6));
            """, connection, transaction);
        command.Parameters.AddWithValue("@operation", operation);
        command.Parameters.AddWithValue("@actorType", actorType);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@subjectType", subjectType);
        command.Parameters.AddWithValue("@subjectId", subjectId);
        command.Parameters.AddWithValue("@event", clientEventId);
        try { await command.ExecuteNonQueryAsync(token); }
        catch (MySqlException error) when (clientEventId is not null && error.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // Only client observation IDs participate in deduplication.
        }
    }
}
