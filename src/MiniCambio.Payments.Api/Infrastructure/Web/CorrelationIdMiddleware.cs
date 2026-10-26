using System.Text.RegularExpressions;

namespace MiniCambio.Payments.Api.Infrastructure.Web;

/// <summary>
/// Lê o <c>X-Correlation-Id</c> da requisição (ou gera um novo), devolve no response e o coloca no escopo de log,
/// para que todas as linhas de log da requisição carreguem o mesmo id.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        // Só aceita ids "bem comportados" vindos de fora, para não levar lixo para os logs.
        if (correlationId is null || !ValidCorrelationId().IsMatch(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string? GetCorrelationId(HttpContext context) => context.Items[HeaderName] as string;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ValidCorrelationId();
}
