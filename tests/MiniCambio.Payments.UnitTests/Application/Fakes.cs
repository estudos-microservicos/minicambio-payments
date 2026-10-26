using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.UnitTests.Application;

internal sealed class InMemoryPaymentRepository : IPaymentRepository
{
    public List<Payment> Payments { get; } = [];

    /// <summary>
    /// Simula a corrida: no próximo <see cref="TryAddAsync"/>, este pagamento é gravado antes, como se uma requisição
    /// concorrente para a mesma ordem tivesse passado na frente.
    /// </summary>
    public Payment? ConcurrentInsert { get; set; }

    public Task<Payment?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Payments.FirstOrDefault(x => x.Id == id));

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Payments.FirstOrDefault(x => x.OrderId == orderId));

    public Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (ConcurrentInsert is not null)
        {
            Payments.Add(ConcurrentInsert);
            ConcurrentInsert = null;
        }

        // Mesmo efeito do índice único em order_id.
        if (Payments.Any(x => x.OrderId == payment.OrderId))
        {
            return Task.FromResult(false);
        }

        Payments.Add(payment);
        return Task.FromResult(true);
    }
}
