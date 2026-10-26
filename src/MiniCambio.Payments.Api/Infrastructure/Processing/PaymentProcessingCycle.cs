using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Domain;
using MiniCambio.Payments.Api.Infrastructure.Persistence;

namespace MiniCambio.Payments.Api.Infrastructure.Processing;

/// <summary>Quantos pagamentos um ciclo reservou (→ Processing) e quantos finalizou (→ Completed/Failed).</summary>
public sealed record PaymentProcessingCycleResult(int Claimed, int Finished);

/// <summary>
/// Um ciclo do processamento mockado, em duas etapas:
/// <list type="number">
/// <item><b>Claim</b>: reserva até <see cref="BatchSize"/> pagamentos <c>Pending</c> e os marca como <c>Processing</c>;</item>
/// <item><b>Conclusão</b>: pagamentos <c>Processing</c> há mais de <c>Payments__SimulatedDelaySeconds</c> recebem o
/// resultado da <see cref="MockPaymentOutcomePolicy"/> (<c>Completed</c> ou <c>Failed</c>).</item>
/// </list>
/// Fica separado do <see cref="PaymentProcessingWorker"/> (que só o chama em loop) para que os testes possam rodar
/// um ciclo de forma determinística.
/// </summary>
public sealed class PaymentProcessingCycle(
    PaymentsDbContext db,
    MockPaymentOutcomePolicy outcomePolicy,
    IOptions<PaymentsOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentProcessingCycle> logger)
{
    /// <summary>Máximo de pagamentos tratados em cada etapa de um ciclo.</summary>
    public const int BatchSize = 10;

    public async Task<PaymentProcessingCycleResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimPendingAsync(cancellationToken);
        var finished = await FinishProcessingAsync(cancellationToken);
        return new PaymentProcessingCycleResult(claimed, finished);
    }

    private Task<int> ClaimPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var pending = nameof(PaymentStatus.Pending);

        return ProcessLockedBatchAsync(
            "reservar",
            db.Payments.FromSql(
                $"""
                SELECT * FROM payments
                WHERE status = {pending}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """),
            payment => payment.StartProcessing(now),
            cancellationToken);
    }

    private Task<int> FinishProcessingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var startedUntil = now.AddSeconds(-options.Value.SimulatedDelaySeconds);
        var processing = nameof(PaymentStatus.Processing);

        return ProcessLockedBatchAsync(
            "finalizar",
            db.Payments.FromSql(
                $"""
                SELECT * FROM payments
                WHERE status = {processing} AND processing_started_at <= {startedUntil}
                ORDER BY processing_started_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """),
            payment => payment.ApplyOutcome(outcomePolicy.Decide(payment), now),
            cancellationToken);
    }

    /// <summary>
    /// Trava um lote com <c>SELECT ... FOR UPDATE SKIP LOCKED</c> dentro de uma transação, aplica a transição em
    /// cada pagamento e grava item a item.
    /// </summary>
    /// <remarks>
    /// O <c>SKIP LOCKED</c> faz cada execução pular as linhas já travadas por outra transação em andamento. Quando o
    /// serviço for escalado no Kubernetes (várias réplicas, cada uma com seu worker), réplicas diferentes pegam lotes
    /// diferentes e nunca processam o mesmo pagamento, sem uma esperar pela outra.
    /// </remarks>
    private async Task<int> ProcessLockedBatchAsync(
        string stage,
        IQueryable<Payment> lockingQuery,
        Action<Payment> transition,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Sem composição LINQ por cima do FromSql: o SQL (com o FOR UPDATE) vai ao banco exatamente como escrito.
        var batch = await lockingQuery.ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var payment in batch)
        {
            try
            {
                transition(payment);

                // Dentro de uma transação, o EF cria um savepoint a cada SaveChanges: se a gravação deste item
                // falhar, só ele é desfeito e a transação continua válida para os próximos.
                await db.SaveChangesAsync(cancellationToken);
                processed++;
                LogTransition(payment);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Erro em um item não pode derrubar o worker: loga e segue para o próximo. O pagamento fica como
                // estava no banco e volta a ser elegível no próximo ciclo.
                logger.LogError(ex, "Falha ao {Stage} o pagamento {PaymentId}; seguindo para o próximo", stage, payment.Id);
                db.Entry(payment).State = EntityState.Detached;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return processed;
    }

    private void LogTransition(Payment payment)
    {
        if (payment.FailureReason is null)
        {
            logger.LogInformation(
                "Pagamento {PaymentId} da ordem {OrderId} agora está {Status}",
                payment.Id,
                payment.OrderId,
                payment.Status);
        }
        else
        {
            logger.LogInformation(
                "Pagamento {PaymentId} da ordem {OrderId} agora está {Status} ({FailureReason})",
                payment.Id,
                payment.OrderId,
                payment.Status,
                payment.FailureReason);
        }
    }
}
