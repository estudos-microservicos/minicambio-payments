using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniCambio.Payments.Api.Application.Payments;

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>
/// Pedidos de pagamento chegando pelo Kafka: o JSON da mensagem é lido como <see cref="CreatePaymentRequest"/> (como o
/// consumidor faz) e entregue ao <see cref="PaymentRequestMessageHandler"/>, gravando no Postgres real.
/// </summary>
public class PaymentRequestMessagesTests(PaymentsApiFactory factory)
{
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);

    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task NewRequest_CreatesPendingPayment()
    {
        var orderId = Guid.NewGuid();

        var outcome = await HandleAsync(PaymentRequests.Body(orderId));

        Assert.Equal(PaymentRequestOutcome.Created, outcome);
        var payment = await client.GetFromJsonAsync<JsonElement>(
            $"{PaymentRequests.Route}?orderId={orderId}", TestContext.Current.CancellationToken);
        Assert.Equal("Pending", payment.GetProperty("status").GetString());
        Assert.Equal(1500.50m, payment.GetProperty("foreignAmount").GetDecimal());
    }

    [Fact]
    public async Task SameRequestTwice_KeepsASinglePayment()
    {
        var orderId = Guid.NewGuid();
        await HandleAsync(PaymentRequests.Body(orderId));

        var outcome = await HandleAsync(PaymentRequests.Body(orderId));

        Assert.Equal(PaymentRequestOutcome.AlreadyExists, outcome);
    }

    [Fact]
    public async Task InvalidRequest_IsRejectedWithoutCreatingPayment()
    {
        var orderId = Guid.NewGuid();

        var outcome = await HandleAsync(PaymentRequests.Body(orderId, beneficiaryCountry: "Brasil"));

        Assert.Equal(PaymentRequestOutcome.Rejected, outcome);
        var response = await client.GetAsync($"{PaymentRequests.Route}?orderId={orderId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<PaymentRequestOutcome> HandleAsync(object body)
    {
        // Ida e volta pelo JSON, como a mensagem que o Orders publica.
        var request = JsonSerializer.Deserialize<CreatePaymentRequest>(JsonSerializer.Serialize(body, MessageJson), MessageJson)!;

        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentRequestMessageHandler>()
            .HandleAsync(request, TestContext.Current.CancellationToken);
    }
}
