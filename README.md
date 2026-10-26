# minicambio-payments

Serviço **Payments** do MiniCâmbio, um sistema de câmbio simplificado usado como projeto de estudo.
Recebe do serviço Orders as solicitações de pagamento das ordens confirmadas e as processa de forma assíncrona e
**mockada**: nenhum provedor real é chamado, o resultado sai de regras fixas.

## Como funciona

1. O Orders chama `POST /api/v1/payments`; o pagamento é gravado em `Pending` e a API responde `202 Accepted`.
2. O `PaymentProcessingWorker` (um `BackgroundService`) roda um ciclo a cada `Payments__WorkerIntervalSeconds`:
   - **claim**: reserva até 10 pagamentos `Pending` com `SELECT ... FOR UPDATE SKIP LOCKED` numa transação e os
     marca como `Processing` (preenchendo `processingStartedAt`);
   - **conclusão**: pagamentos em `Processing` há pelo menos `Payments__SimulatedDelaySeconds` recebem o resultado
     das regras do mock (`Completed` ou `Failed`), também com `SKIP LOCKED`.
3. O Orders acompanha o pagamento por polling (`GET /api/v1/payments/{id}`).

O `SKIP LOCKED` faz cada execução pular as linhas já travadas por outra, então várias réplicas do serviço (no
Kubernetes, numa etapa futura) nunca processam o mesmo pagamento. Erro em um pagamento é logado e não interrompe os
demais nem derruba o worker.

```
Pending ──► Processing ──► Completed   (providerReference = MOCK-XXXXXXXX)
                      └──► Failed      (failureReason)
```

## Regras do mock

Avaliadas nesta ordem; a primeira que casar define o resultado:

| # | Condição | Resultado |
|---|---|---|
| 1 | Nome do beneficiário contém `FAIL` (sem diferenciar maiúsculas) | `Failed` / `BENEFICIARY_REJECTED` |
| 2 | País do beneficiário está em `Payments__BlockedCountries` (padrão `KP,IR`) | `Failed` / `COMPLIANCE_BLOCKED` |
| 3 | `foreignAmount` maior que `Payments__MaxAmount` (padrão `100000`) | `Failed` / `LIMIT_EXCEEDED` |
| 4 | Caso contrário | `Completed`, com `providerReference` = `MOCK-` + 8 caracteres alfanuméricos |

## Idempotência

Existe no máximo um pagamento por ordem (índice único em `order_id`). Um novo `POST` com um `orderId` que já tem
pagamento devolve `200 OK` com o pagamento existente, sem criar outro (mesmo que o corpo seja diferente). Sob
concorrência, quando duas requisições passam juntas pela verificação, o índice único barra a segunda (Postgres
`23505`) e ela também devolve o registro existente. Por isso o Orders pode reenviar a solicitação com segurança.

## Endpoints

| Método | Rota | Resposta |
|---|---|---|
| POST | `/api/v1/payments` | body `{ orderId, currency, foreignAmount, beneficiary: { name, country, accountNumber } }` → `202` + `Location: /api/v1/payments/{id}` (status `Pending`); `200` com o existente se a ordem já tem pagamento; `400` se inválido |
| GET | `/api/v1/payments/{id}` | `200` ou `404` |
| GET | `/api/v1/payments?orderId={orderId}` | `200` com o pagamento da ordem; `404`; `400` se `orderId` ausente ou inválido |
| GET | `/health/live` | `200` se o processo responde (sem dependências) |
| GET | `/health/ready` | `200` se o banco responde |

Resposta de pagamento:

```json
{
  "id": "01a17a55-d520-7323-882b-495a0aeaa3b5",
  "orderId": "f98663a6-1ef7-4e9f-82d6-958cf5d42e60",
  "currency": "USD",
  "foreignAmount": 100.00,
  "beneficiary": { "name": "Maria Silva", "country": "BR", "accountNumber": "12345-6" },
  "status": "Completed",
  "failureReason": null,
  "providerReference": "MOCK-I0I3A1ZO",
  "processingStartedAt": "2026-10-26T22:30:08.427579+00:00",
  "createdAt": "2026-10-26T22:30:07.904487+00:00",
  "updatedAt": "2026-10-26T22:30:13.425649+00:00",
  "servedBy": "dd7e6d0c5fa1"
}
```

Validação do `POST` (erros `400` por campo, chaves em camelCase): `orderId` obrigatório; `currency` com 3 letras
maiúsculas (ISO 4217); `foreignAmount` maior que zero e com no máximo 2 casas; `beneficiary.name` obrigatório até
120 caracteres; `beneficiary.country` com 2 letras maiúsculas (ISO 3166-1 alpha-2); `beneficiary.accountNumber`
obrigatório até 34 caracteres.

Erros seguem ProblemDetails (`title` e `detail` em português, extensão `correlationId`; `400` traz `errors` por
campo). Todas as respostas devolvem o header `X-Correlation-Id` (o recebido ou um novo). `servedBy` traz o nome da
máquina que atendeu. Em `Development`, a documentação fica em `/scalar` (OpenAPI em `/openapi/v1.json`).

## Variáveis de ambiente

| Variável | Padrão | Descrição |
|---|---|---|
| `ConnectionStrings__Default` | — (obrigatória) | Connection string do Postgres |
| `Database__ApplyMigrationsOnStartup` | `false` | Aplica as migrations ao subir (`true` no compose) |
| `Payments__WorkerIntervalSeconds` | `5` | Intervalo entre os ciclos do worker, em segundos |
| `Payments__SimulatedDelaySeconds` | `3` | Tempo mínimo em `Processing` antes do resultado, em segundos |
| `Payments__BlockedCountries` | `KP,IR` | Países bloqueados (ISO 3166-1 alpha-2) separados por vírgula; vazio = nenhum |
| `Payments__MaxAmount` | `100000` | Valor máximo aceito pelo mock (em moeda estrangeira) |

As opções são validadas na inicialização: valores fora do intervalo impedem a API de subir. Em `Development`, o
`appsettings.Development.json` já aponta para o Postgres do `compose.dev.yml` e liga as migrations.

## Requisitos

- .NET SDK 10
- Docker Desktop (Postgres local e testes de integração com Testcontainers)

## Rodar localmente

```powershell
docker compose -f compose.dev.yml up -d             # só o Postgres, na porta 5434
dotnet run --project src/MiniCambio.Payments.Api    # http://localhost:5002 (Scalar em /scalar)
```

O `compose.dev.yml` usa a mesma porta e as mesmas credenciais do `payments-db` do `minicambio-infra`: não suba os
dois ao mesmo tempo.

Exemplo de pagamento que termina recusado:

```powershell
$body = @{
  orderId       = [guid]::NewGuid()
  currency      = 'USD'
  foreignAmount = 100
  beneficiary   = @{ name = 'FAIL Teste'; country = 'BR'; accountNumber = '12345-6' }
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5002/api/v1/payments -ContentType 'application/json' -Body $body
```

## Testes

```powershell
dotnet test MiniCambio.Payments.slnx
```

- `tests/MiniCambio.Payments.UnitTests`: regras do mock (`MockPaymentOutcomePolicy`, com `Random` de semente
  fixa), transições de status do `Payment`, validação, options e handlers (com fakes).
- `tests/MiniCambio.Payments.IntegrationTests`: endpoints contra um Postgres real (Testcontainers), incluindo a
  idempotência sob concorrência, e o ciclo de processamento (`Pending → Processing → Completed/Failed`) com
  `FakeTimeProvider`. O worker hospedado é removido nesses testes e o ciclo é chamado diretamente, para que o
  resultado seja determinístico; os testes do ciclo usam um banco exclusivo por teste.

## Migrations

```powershell
dotnet tool exec dotnet-ef@10.0.14 --yes -- migrations add <Nome> --project src/MiniCambio.Payments.Api --output-dir Infrastructure/Persistence/Migrations
```

## Docker

```powershell
docker build -t minicambio-payments-api .
```

A imagem roda como usuário não-root e escuta na porta `8080`. Para subir o sistema inteiro (bancos, APIs e
frontend), use o `docker-compose.yml` do repositório `minicambio-infra`.
