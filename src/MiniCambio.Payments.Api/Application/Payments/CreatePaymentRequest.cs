namespace MiniCambio.Payments.Api.Application.Payments;

/// <summary>
/// Corpo do <c>POST /api/v1/payments</c>. Os campos são anuláveis para que a validação devolva mensagens por campo
/// em vez de um erro genérico de desserialização.
/// </summary>
public sealed record CreatePaymentRequest(
    Guid? OrderId,
    string? Currency,
    decimal? ForeignAmount,
    BeneficiaryRequest? Beneficiary);

public sealed record BeneficiaryRequest(string? Name, string? Country, string? AccountNumber);
