using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Domain;
using MiniCambio.Payments.Api.Infrastructure.Persistence;

namespace MiniCambio.Payments.Api.Infrastructure.Processing;

/// <summary>
/// Quantos pagamentos um ciclo reservou (→ Processing), quantos finalizou (→ Completed/Failed) e quantos resultados
/// publicou no Kafka.
/// </summary>
public sealed record PaymentProcessingCycleResult(int Claimed, int Finished, int Published);

/// <summary>
/// Um ciclo do processamento mockado, em três etapas:
/// <list type="number">
/// <item><b>Claim</b>: reserva até <see cref="BatchSize"/> pagamentos <c>Pending</c> e os marca como <c>Processing</c>;</item>
/// <item><b>Conclusão</b>: pagamentos <c>Processing</c> há mais de <c>Payments__SimulatedDelaySeconds</c> recebem o
/// resultado da <see cref="MockPaymentOutcomePolicy"/> (<c>Completed</c> ou <c>Failed</c>);</item>
/// <item><b>Publicação</b>: pagamentos finalizados com <c>result_published_at</c> vazio têm o resultado publicado no
/// Kafka para o Orders. Gravar o resultado e publicar são passos separados de propósito (padrão outbox): se o Kafka
/// estiver fora, o resultado já está salvo no banco e é publicado num ciclo seguinte, sem se perder.</item>
/// </list>
/// Fica separado do <see cref="PaymentProcessingWorker"/> (que só o chama em loop) para que os testes possam rodar
/// um ciclo de forma determinística.
/// </summary>
public sealed class PaymentProcessingCycle(
    PaymentsDbContext db,
    MockPaymentOutcomePolicy outcomePolicy,
    IPaymentResultPublisher resultPublisher,
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
        var published = await PublishResultsAsync(cancellationToken);
        return new PaymentProcessingCycleResult(claimed, finished, published);
    }

    private async Task<int> PublishResultsAsync(CancellationToken cancellationToken)
    {
        var completed = nameof(PaymentStatus.Completed);
        var failed = nameof(PaymentStatus.Failed);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.Payments.FromSql(
                $"""
                SELECT * FROM payments
                WHERE status IN ({completed}, {failed}) AND result_published_at IS NULL
                ORDER BY updated_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var payment in batch)
        {
            try
            {
                await resultPublisher.PublishAsync(PaymentResultMessage.From(payment), cancellationToken);
                payment.MarkResultPublished(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                published++;
            }
            catch (MessagePublishException ex)
            {
                // Kafka fora: os próximos do lote também falhariam (5 s cada). Para aqui; o próximo ciclo tenta de novo.
                logger.LogWarning(ex, "Kafka indisponível ao publicar o resultado do pagamento {PaymentId}", payment.Id);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Publicado, mas a marcação não foi gravada: será publicado de novo, e o Orders ignora a repetição.
                logger.LogError(ex, "Falha ao registrar a publicação do pagamento {PaymentId}", payment.Id);
                db.Entry(payment).State = EntityState.Detached;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return published;
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
