using MySqlConnector;

namespace AstralOath.Server.Arena;

internal sealed class ArenaRoomLeaseWorker(ArenaStore store, ILogger<ArenaRoomLeaseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!store.IsConfigured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try { await store.SweepExpiredRooms(deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (MySqlException error)
            {
                logger.LogWarning("Arena room expiration sweep failed with MySQL error {ErrorNumber}.", error.Number);
            }
        }
    }
}
