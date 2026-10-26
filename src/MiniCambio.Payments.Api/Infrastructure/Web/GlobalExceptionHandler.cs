using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Infrastructure.Web;

/// <summary>Converte exceções em ProblemDetails com textos que o frontend pode exibir ao usuário.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                "Requisição inválida",
                "Não foi possível ler a requisição. Verifique se o JSON enviado está correto."),
            ResourceNotFoundException notFound => (
                StatusCodes.Status404NotFound,
                "Recurso não encontrado",
                notFound.Message),
            DomainException domain => (
                StatusCodes.Status422UnprocessableEntity,
                "Operação não permitida",
                domain.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Erro inesperado",
                "Ocorreu um erro inesperado. Tente novamente em instantes."),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Erro não tratado ao processar a requisição");
        }
        else
        {
            logger.LogInformation("Requisição recusada com {StatusCode}: {Detail}", status, detail);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
