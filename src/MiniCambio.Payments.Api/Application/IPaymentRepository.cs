using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Application;

public interface IPaymentRepository
{
    Task<Payment?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste um pagamento novo. Retorna <c>false</c>, sem lançar exceção, quando já existe pagamento para a mesma
    /// ordem (violação do índice único em <c>order_id</c>), o que acontece quando duas requisições concorrentes
    /// passam ao mesmo tempo pela verificação de existência.
    /// </summary>
    Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken);
}
