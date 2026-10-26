namespace MiniCambio.Payments.Api.Domain;

/// <summary>
/// Pagamento solicitado pelo Orders para uma ordem. Existe no máximo um pagamento por ordem (<see cref="OrderId"/>).
/// O status só muda pelos métodos de transição, que lançam <see cref="DomainException"/> se a transição for inválida.
/// </summary>
public sealed class Payment
{
    // Construtor usado pelo EF Core.
    private Payment()
    {
        Currency = string.Empty;
        BeneficiaryName = string.Empty;
        BeneficiaryCountry = string.Empty;
        BeneficiaryAccount = string.Empty;
    }

    private Payment(
        Guid id,
        Guid orderId,
        string currency,
        decimal foreignAmount,
        string beneficiaryName,
        string beneficiaryCountry,
        string beneficiaryAccount,
        DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        Currency = currency;
        ForeignAmount = foreignAmount;
        BeneficiaryName = beneficiaryName;
        BeneficiaryCountry = beneficiaryCountry;
        BeneficiaryAccount = beneficiaryAccount;
        Status = PaymentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string Currency { get; private set; }

    public decimal ForeignAmount { get; private set; }

    public string BeneficiaryName { get; private set; }

    public string BeneficiaryCountry { get; private set; }

    public string BeneficiaryAccount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public string? ProviderReference { get; private set; }

    public DateTimeOffset? ProcessingStartedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Cria um pagamento em <see cref="PaymentStatus.Pending"/>.</summary>
    public static Payment Create(
        Guid orderId,
        string currency,
        decimal foreignAmount,
        string beneficiaryName,
        string beneficiaryCountry,
        string beneficiaryAccount,
        DateTimeOffset now)
    {
        if (orderId == Guid.Empty)
        {
            throw new DomainException("O pagamento precisa estar associado a uma ordem.");
        }

        if (foreignAmount <= 0)
        {
            throw new DomainException("O valor do pagamento deve ser maior que zero.");
        }

        return new Payment(
            Guid.CreateVersion7(now),
            orderId,
            currency,
            foreignAmount,
            beneficiaryName,
            beneficiaryCountry,
            beneficiaryAccount,
            now);
    }

    /// <summary><c>Pending → Processing</c>: o worker reservou o pagamento.</summary>
    public void StartProcessing(DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Pending, "iniciar o processamento de");

        Status = PaymentStatus.Processing;
        ProcessingStartedAt = now;
        UpdatedAt = now;
    }

    /// <summary><c>Processing → Completed</c>, com a referência devolvida pelo provedor.</summary>
    public void Complete(string providerReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        EnsureStatus(PaymentStatus.Processing, "concluir");

        Status = PaymentStatus.Completed;
        ProviderReference = providerReference;
        UpdatedAt = now;
    }

    /// <summary><c>Processing → Failed</c>, com o código do motivo (ver <see cref="PaymentFailureReasons"/>).</summary>
    public void Fail(string failureReason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        EnsureStatus(PaymentStatus.Processing, "recusar");

        Status = PaymentStatus.Failed;
        FailureReason = failureReason;
        UpdatedAt = now;
    }

    /// <summary>Aplica o resultado decidido pela política de processamento.</summary>
    public void ApplyOutcome(PaymentOutcome outcome, DateTimeOffset now)
    {
        if (outcome.Status == PaymentStatus.Completed)
        {
            Complete(outcome.ProviderReference!, now);
        }
        else
        {
            Fail(outcome.FailureReason!, now);
        }
    }

    private void EnsureStatus(PaymentStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Não é possível {action} um pagamento com status {Status}.");
        }
    }
}
