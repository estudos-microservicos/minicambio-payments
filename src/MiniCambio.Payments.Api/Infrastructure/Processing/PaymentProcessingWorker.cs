using Microsoft.Extensions.Options;
using MiniCambio.Payments.Api.Application;

namespace MiniCambio.Payments.Api.Infrastructure.Processing;

/// <summary>
/// Processa os pagamentos em segundo plano: roda um <see cref="PaymentProcessingCycle"/> a cada
/// <c>Payments__WorkerIntervalSeconds</c>. Cada ciclo usa um escopo de DI próprio (DbContext novo) e qualquer erro é
/// logado sem derrubar o worker: o próximo ciclo tenta de novo.
/// </summary>
public sealed class PaymentProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentsOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.WorkerIntervalSeconds);
        logger.LogInformation("Worker de processamento de pagamentos iniciado (intervalo de {Interval})", interval);

        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal da aplicação.
        }
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cycle = scope.ServiceProvider.GetRequiredService<PaymentProcessingCycle>();
            var result = await cycle.RunOnceAsync(stoppingToken);

            if (result.Claimed > 0 || result.Finished > 0)
            {
                logger.LogInformation(
                    "Ciclo de processamento: {Claimed} pagamento(s) reservado(s), {Finished} finalizado(s)",
                    result.Claimed,
                    result.Finished);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Falha no ciclo de processamento de pagamentos; nova tentativa no próximo ciclo");
        }
    }
}
