using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniCambio.Payments.Api.Infrastructure.Persistence;

namespace MiniCambio.Payments.IntegrationTests;

public class PaymentsEndpointsTests(PaymentsApiFactory factory)
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task CreatePayment_NewOrder_Returns202WithLocationAndPendingPayment()
    {
        var orderId = Guid.NewGuid();

        var response = await PaymentRequests.PostAsync(client, PaymentRequests.Body(orderId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payment = await response.ReadJsonAsync();
        var id = payment.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/payments/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(orderId, payment.GetProperty("orderId").GetGuid());
        Assert.Equal("USD", payment.GetProperty("currency").GetString());
        Assert.Equal(1500.50m, payment.GetProperty("foreignAmount").GetDecimal());
        Assert.Equal("Pending", payment.GetProperty("status").GetString());

        var beneficiary = payment.GetProperty("beneficiary");
        Assert.Equal("Maria Silva", beneficiary.GetProperty("name").GetString());
        Assert.Equal("BR", beneficiary.GetProperty("country").GetString());
        Assert.Equal("12345-6", beneficiary.GetProperty("accountNumber").GetString());

        Assert.Equal(JsonValueKind.Null, payment.GetProperty("failureReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("providerReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("processingStartedAt").ValueKind);
        Assert.Equal(payment.GetProperty("createdAt").GetDateTimeOffset(), payment.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.False(string.IsNullOrWhiteSpace(payment.GetProperty("servedBy").GetString()));
    }

    [Fact]
    public async Task CreatePayment_SameOrderIdTwice_Returns200WithExistingPayment()
    {
        var orderId = Guid.NewGuid();
        var first = await PaymentRequests.PostAsync(client, PaymentRequests.Body(orderId));

        var second = await PaymentRequests.PostAsync(client, PaymentRequests.Body(orderId, foreignAmount: 10m));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var created = await first.ReadJsonAsync();
        var replayed = await second.ReadJsonAsync();
        Assert.Equal(created.GetProperty("id").GetGuid(), replayed.GetProperty("id").GetGuid());
        Assert.Equal(1500.50m, replayed.GetProperty("foreignAmount").GetDecimal());
        Assert.Equal(1, await CountPaymentsForOrderAsync(orderId));
    }

    [Fact]
    public async Task CreatePayment_ConcurrentRequestsWithSameOrderId_CreatesSinglePayment()
    {
        var orderId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => PaymentRequests.PostAsync(client, PaymentRequests.Body(orderId))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Accepted, HttpStatusCode.OK }));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.ReadJsonAsync()).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CountPaymentsForOrderAsync(orderId));
    }

    [Fact]
    public async Task CreatePayment_InvalidRequest_Returns400WithCamelCaseFieldErrors()
    {
        var body = new
        {
            orderId = Guid.Empty,
            currency = "usd",
            foreignAmount = 10.123m,
            beneficiary = new { name = "", country = "Brasil", accountNumber = new string('9', 35) },
        };

        var response = await PaymentRequests.PostAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Dados inválidos", problem.GetProperty("title").GetString());
        Assert.Equal("Corrija os campos destacados e tente novamente.", problem.GetProperty("detail").GetString());
        Assert.Equal(
            ["beneficiary.accountNumber", "beneficiary.country", "beneficiary.name", "currency", "foreignAmount", "orderId"],
            problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreatePayment_WithoutBeneficiary_Returns400WithBeneficiaryError()
    {
        var response = await PaymentRequests.PostAsync(client, new { orderId = Guid.NewGuid(), currency = "USD", foreignAmount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.Equal(["beneficiary"], errors.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task CreatePayment_MalformedJson_Returns400ProblemDetails()
    {
        using var content = new StringContent("{ \"orderId\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(PaymentRequests.Route, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Requisição inválida", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetPayment_Existing_Returns200WithPayment()
    {
        var id = await PaymentRequests.CreateAsync(client);

        var response = await client.GetAsync($"{PaymentRequests.Route}/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.ReadJsonAsync();
        Assert.Equal(id, payment.GetProperty("id").GetGuid());
        Assert.Equal("Pending", payment.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payment.GetProperty("servedBy").GetString()));
    }

    [Fact]
    public async Task GetPayment_Unknown_Returns404ProblemDetails()
    {
        var response = await client.GetAsync($"{PaymentRequests.Route}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Recurso não encontrado", problem.GetProperty("title").GetString());
        Assert.Equal("Pagamento não encontrado.", problem.GetProperty("detail").GetString());
        Assert.Equal(
            Assert.Single(response.Headers.GetValues("X-Correlation-Id")),
            problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GetPaymentByOrder_Existing_Returns200WithPayment()
    {
        var orderId = Guid.NewGuid();
        var created = await (await PaymentRequests.PostAsync(client, PaymentRequests.Body(orderId))).ReadJsonAsync();

        var response = await client.GetAsync($"{PaymentRequests.Route}?orderId={orderId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.ReadJsonAsync();
        Assert.Equal(created.GetProperty("id").GetGuid(), payment.GetProperty("id").GetGuid());
        Assert.Equal(orderId, payment.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task GetPaymentByOrder_UnknownOrder_Returns404()
    {
        var response = await client.GetAsync($"{PaymentRequests.Route}?orderId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Recurso não encontrado", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?orderId=")]
    [InlineData("?orderId=abc")]
    [InlineData("?orderId=00000000-0000-0000-0000-000000000000")]
    public async Task GetPaymentByOrder_MissingOrInvalidOrderId_Returns400WithOrderIdError(string query)
    {
        var response = await client.GetAsync($"{PaymentRequests.Route}{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Dados inválidos", problem.GetProperty("title").GetString());
        Assert.Equal(["orderId"], problem.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    private async Task<int> CountPaymentsForOrderAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await db.Payments.CountAsync(x => x.OrderId == orderId, TestContext.Current.CancellationToken);
    }
}
