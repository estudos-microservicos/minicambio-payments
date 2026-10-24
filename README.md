# minicambio-payments

Serviço **Payments** do MiniCâmbio, um sistema de câmbio simplificado usado como projeto de estudo.
Recebe solicitações de pagamento e as processa de forma assíncrona e mockada.

> Repositório em construção (Fase 0: estrutura). Endpoints, variáveis de ambiente e instruções completas
> entram nas próximas fases.

## Requisitos

- .NET SDK 10
- Docker Desktop (testes de integração usam Testcontainers)

## Build e testes

```powershell
dotnet build MiniCambio.Payments.slnx
dotnet test MiniCambio.Payments.slnx
```

## Rodar localmente

```powershell
dotnet run --project src/MiniCambio.Payments.Api
```

A API sobe em `http://localhost:5002`.
