namespace MiniCambio.Payments.Api.Domain;

/// <summary>
/// Ciclo de vida de um pagamento: <c>Pending → Processing → Completed | Failed</c>.
/// <see cref="Completed"/> e <see cref="Failed"/> são estados finais.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Recebido, aguardando o worker.</summary>
    Pending,

    /// <summary>Reservado pelo worker; o resultado sai após o atraso simulado.</summary>
    Processing,

    /// <summary>Pago (mock), com <c>providerReference</c>.</summary>
    Completed,

    /// <summary>Recusado (mock), com <c>failureReason</c>.</summary>
    Failed,
}
