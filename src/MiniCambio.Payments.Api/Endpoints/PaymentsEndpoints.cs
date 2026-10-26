using Microsoft.AspNetCore.Http.HttpResults;
using MiniCambio.Payments.Api.Application.Payments;

namespace MiniCambio.Payments.Api.Endpoints;

public static class PaymentsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").WithTags("Payments");

        group.MapPost(
                "/",
                async Task<Results<Accepted<PaymentResponse>, Ok<PaymentResponse>>> (
                    CreatePaymentRequest request,
                    CreatePaymentHandler handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(request, cancellationToken);

                    // 202: aceito em Pending, o worker processa de forma assíncrona (acompanhe pelo Location).
                    // 200: a ordem já tinha pagamento (replay idempotente); devolve o existente sem criar outro.
                    return result.Created
                        ? TypedResults.Accepted($"/api/v1/payments/{result.Payment.Id}", result.Payment)
                        : TypedResults.Ok(result.Payment);
                })
            .AddEndpointFilter<ValidationFilter<CreatePaymentRequest>>()
            .WithName("CreatePayment")
            .WithSummary("Solicita o pagamento de uma ordem (idempotente por orderId)")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetPaymentHandler handler, CancellationToken cancellationToken) =>
                TypedResults.Ok(await handler.HandleAsync(id, cancellationToken)))
            .WithName("GetPayment")
            .WithSummary("Consulta um pagamento")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet(
                "/",
                async ([AsParameters] GetPaymentByOrderQuery query, GetPaymentByOrderHandler handler, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await handler.HandleAsync(Guid.Parse(query.OrderId!), cancellationToken)))
            .AddEndpointFilter<ValidationFilter<GetPaymentByOrderQuery>>()
            .WithName("GetPaymentByOrder")
            .WithSummary("Consulta o pagamento de uma ordem (?orderId=)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
