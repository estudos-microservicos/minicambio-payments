using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace MiniCambio.Payments.Api.Application.Payments;

/// <summary>
/// Query string do <c>GET /api/v1/payments?orderId=...</c> (ligada com <c>[AsParameters]</c>). O <c>orderId</c> chega
/// como texto para que um valor ausente ou inválido vire erro de campo (400 com <c>errors.orderId</c>) em vez de uma
/// falha genérica de binding. O nome explícito mantém o parâmetro em camelCase no OpenAPI.
/// </summary>
public sealed record GetPaymentByOrderQuery([FromQuery(Name = "orderId")] string? OrderId);

public sealed class GetPaymentByOrderQueryValidator : AbstractValidator<GetPaymentByOrderQuery>
{
    public GetPaymentByOrderQueryValidator()
    {
        RuleFor(x => x.OrderId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Informe o id da ordem.")
            .Must(value => Guid.TryParse(value, out var orderId) && orderId != Guid.Empty)
            .WithMessage("O id da ordem deve ser um GUID válido.");
    }
}

public sealed class GetPaymentByOrderHandler(IPaymentRepository payments)
{
    public async Task<PaymentResponse> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByOrderIdAsync(orderId, cancellationToken)
            ?? throw new ResourceNotFoundException("Não existe pagamento para a ordem informada.");

        return PaymentResponse.From(payment);
    }
}
