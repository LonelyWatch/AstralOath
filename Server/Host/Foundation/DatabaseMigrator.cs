using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace AstralOath.Server.Foundation;

public static class DatabaseMigrator
{
    public static async Task ApplyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var settings = new MySqlConnectionStringBuilder(connectionString) { Pooling = false };
        if (string.IsNullOrWhiteSpace(settings.Database))
            throw new InvalidOperationException("The migration connection must name a database.");

        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // A dedicated unpooled connection releases the session lock on disconnect.
        var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AstralOath:" + settings.Database.ToLowerInvariant())));
        using var acquire = new MySqlCommand("SELECT GET_LOCK(@name, 15);", connection);
        acquire.Parameters.AddWithValue("@name", lockName);
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Could not acquire the database migration lock.");

        using var bootstrap = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS foundation_migrations (
                version VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
                checksum BINARY(32) NOT NULL,
                applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;
            """, connection);
        await bootstrap.ExecuteNonQueryAsync(cancellationToken);

        var assembly = typeof(DatabaseMigrator).Assembly;
        const string prefix = "AstralOath.Server.Foundation.Migrations.";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException("No embedded migrations found.");

        foreach (var resource in resources)
        {
            using var script = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var sql = (await script.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var checksum = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
            var version = resource[prefix.Length..];
            using var lookup = new MySqlCommand("SELECT checksum FROM foundation_migrations WHERE version = @version;", connection);
            lookup.Parameters.AddWithValue("@version", version);
            if (await lookup.ExecuteScalarAsync(cancellationToken) is byte[] appliedChecksum)
            {
                if (!checksum.AsSpan().SequenceEqual(appliedChecksum))
                    throw new InvalidOperationException($"Applied migration {version} has changed.");
                continue;
            }

            // MySQL DDL implicitly commits. Migration scripts must be reentrant
            // if the process stops between the DDL and writing the history row.
            using var apply = new MySqlCommand(sql, connection);
            await apply.ExecuteNonQueryAsync(cancellationToken);
            using var record = new MySqlCommand(
                "INSERT INTO foundation_migrations (version, checksum) VALUES (@version, @checksum);", connection);
            record.Parameters.AddWithValue("@version", version);
            record.Parameters.AddWithValue("@checksum", checksum);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
