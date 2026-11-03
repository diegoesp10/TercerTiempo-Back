using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TercerTiempo.Infrastructure.RealData;

internal interface IRealDataLease
{
    // Este entorno puede descargar (RealData:Ingest), aunque ahora lo esté haciendo otro.
    bool CanIngest { get; }

    // Este proceso es el que descarga ahora.
    bool IsHeld { get; }

    Task WhenHeldAsync(CancellationToken cancellationToken);
}

// Solo un proceso descarga a la vez los datos reales: el que tiene el bloqueo de la base compartida, un sp_getapplock de
// sesión sobre una conexión propia que se queda abierta. Si esa API se para o pierde la conexión, el bloqueo se suelta
// solo y lo coge el siguiente entorno que pueda descargar (RealData:Ingest); los demás lo intentan cada minuto.
internal sealed class RealDataLease(
    RealDataConnections connections,
    IOptions<RealDataOptions> options,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<RealDataLease> logger) : BackgroundService, IRealDataLease
{
    private const string Resource = "TercerTiempo.RealData.Ingest";
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CanIngest => options.Value.Ingest;

    public bool IsHeld
    {
        get { lock (_lock) return _held.Task.IsCompleted; }
    }

    public Task WhenHeldAsync(CancellationToken cancellationToken)
    {
        lock (_lock) return _held.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CanIngest) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sin agrupación: al cerrarla se cierra la sesión y con ella el bloqueo.
                var builder = new SqlConnectionStringBuilder(connections.RealData) { Pooling = false, ApplicationName = "TercerTiempo ingest lease" };
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(stoppingToken);

                if (await TryAcquireAsync(connection, stoppingToken))
                {
                    var since = clock.GetUtcNow();
                    await MarkAsync(connection, since, stoppingToken);
                    SetHeld(true);
                    logger.LogInformation("Este entorno ({Environment}) descarga los datos reales", environment.EnvironmentName);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(Heartbeat, clock, stoppingToken);
                        await MarkAsync(connection, since, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sin conexión con la base de datos reales para el bloqueo de las descargas");
            }

            if (IsHeld)
            {
                SetHeld(false);
                logger.LogWarning("Este entorno deja de descargar los datos reales");
            }

            try
            {
                await Task.Delay(Retry, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<bool> TryAcquireAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;";
        command.Parameters.AddWithValue("@resource", Resource);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    private async Task MarkAsync(SqlConnection connection, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE dbo.RealDataIngester AS t
            USING (SELECT @id AS Id) AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET Environment = @environment, Machine = @machine, Since = @since, HeartbeatAt = @now
            WHEN NOT MATCHED THEN INSERT (Id, Environment, Machine, Since, HeartbeatAt) VALUES (@id, @environment, @machine, @since, @now);
            """;
        command.Parameters.AddWithValue("@id", RealDataIngester.SingletonId);
        command.Parameters.AddWithValue("@environment", environment.EnvironmentName);
        command.Parameters.AddWithValue("@machine", System.Environment.MachineName);
        command.Parameters.AddWithValue("@since", since);
        command.Parameters.AddWithValue("@now", clock.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void SetHeld(bool held)
    {
        lock (_lock)
        {
            if (held) _held.TrySetResult();
            else if (_held.Task.IsCompleted) _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
