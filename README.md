# Brewery Crawl API

Example backend for a workshop on error handling between backend and frontend.
Built with .NET 10 Minimal APIs. Every error is returned as [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457).

Brewery data comes from [Open Brewery DB](https://www.openbrewerydb.org/), with a bundled snapshot as fallback.

## Running

With Docker (no .NET SDK needed):

```sh
docker compose up --build
```

With the .NET 10 SDK:

```sh
dotnet run --project src/BreweryCrawl.Api
```

Then open <http://localhost:8080> for the API docs (Scalar). The logs are in the terminal, or run `docker compose logs -f`.

Run the tests with `dotnet test`.

## The error contract

Every error response has `Content-Type: application/problem+json` and looks like this:

```json
{
  "type": "/errors/brewery.not_found",
  "title": "Brewery not found",
  "status": 404,
  "detail": "No brewery exists with id 'abc'.",
  "instance": "/api/breweries/abc",
  "errorCode": "brewery.not_found",
  "params": { "id": "abc" },
  "traceId": "5a5a07420c958b878dfbf0f0af25ebbb"
}
```

| Field | Use it for |
|---|---|
| `status` | Same as the HTTP status code. |
| `errorCode` | **Switch on this in the frontend**: it is stable. `title` and `detail` are English text for humans and may change. |
| `params` | Values for building your own (translated) message, e.g. `"Fant ikke bryggeriet {id}"`. |
| `traceId` | Show it to the user ("reference: …"). The same id is on every log line for the request. |
| `type` | Links to a description of the error: `GET /errors/{code}`. |

Validation errors (400, `validation.failed`) also have a message and a code per field:

```json
{
  "errors": { "perPage": ["'perPage' must be between 1 and 50."] },
  "errorCodes": { "perPage": [{ "code": "validation.out_of_range", "params": { "min": 1, "max": 50 } }] }
}
```

`GET /errors` lists every error code and validation code. Status codes the framework produces by itself
(e.g. 415) get a generic code such as `http.415`.

Errors are always returned as JSON, even when the client asks for something else (e.g. a browser sending `Accept: text/html`).

### Errors the API can return

| Status | errorCode | How to trigger it |
|---|---|---|
| 400 | `validation.failed` | `GET /api/breweries?perPage=500&type=foo` |
| 400 | `request.malformed` | `GET /api/breweries?perPage=abc` |
| 404 | `brewery.not_found` | `GET /api/breweries/does-not-exist` |
| 404 | `request.not_found` | `GET /api/nope` |
| 405 | `request.method_not_allowed` | `DELETE /api/breweries` |
| 500 | `server.unexpected` | `X-Chaos: fail` |
| 502 | `upstream.unavailable` | `X-Chaos: upstream-down,no-fallback` |
| 503 | `upstream.circuit_open` | `X-Chaos: upstream-circuit-open,no-fallback` |
| 504 | `upstream.timeout` | `X-Chaos: upstream-timeout,no-fallback` (takes about 6 s) |
| 499 | *(no body)* | `X-Chaos: slow`, then abort the request in the client |

## Chaos: trigger errors on demand

Send the `X-Chaos` header, or the `chaos` query parameter (handy in a browser), on any `/api` request.
Combine modes with commas: `?chaos=upstream-down,no-fallback`.

| Mode | Effect |
|---|---|
| `fail` | The request throws an exception → 500. |
| `flaky` | Fails with 500 about half of the time. For practising retries. |
| `slow` | Waits 5 seconds before answering. For loading states, timeouts and `AbortController`. |
| `upstream-down` | Open Brewery DB answers 503. The API retries, then falls back to the snapshot (or 502). |
| `upstream-timeout` | Open Brewery DB never answers. The API times out, then falls back to the snapshot (or 504). |
| `upstream-circuit-open` | Acts as if the circuit breaker is open. Falls back to the snapshot (or 503). |
| `no-fallback` | Turns off the snapshot fallback, so upstream failures reach the client. |

Simulated upstream faults happen outside the resilience pipeline: they don't go through retries, and they never
affect the real circuit breaker, so one person's chaos can't break the API for everyone else. Real failures do
go through retries, timeouts and the circuit breaker; the logs show this when Open Brewery DB is actually down.

The upstream modes also work in `Offline` mode, so the 502/503/504 demos work without internet.

## Data source and fallback

The `X-Data-Source` response header tells where the data came from: `live`, `cache` (5 minutes) or `snapshot`.

Choose the mode with `Breweries__DataSource`:

| Mode | Behaviour |
|---|---|
| `LiveWithFallback` (default) | Calls Open Brewery DB and falls back to the snapshot if the call fails. |
| `Live` | Calls Open Brewery DB only. Failures become 502/503/504. |
| `Offline` | Uses the snapshot only. Works without internet. |

The snapshot (`src/BreweryCrawl.Api/Data/breweries.json`) is the full Open Brewery DB dataset
from [openbrewerydb/openbrewerydb](https://github.com/openbrewerydb/openbrewerydb) (MIT license).

## Configuration

| Setting (environment variable) | Default | |
|---|---|---|
| `Breweries__DataSource` | `LiveWithFallback` | See above. |
| `ErrorHandling__IncludeExceptionDetails` | `false` | Adds exception type, message and stack trace to 500 responses. Try it with `X-Chaos: fail` to see why it must never be on in production. |
| `Logging__Format` | `text` | `json` for structured logs, e.g. in Azure. |
| `Cors__AllowedOrigins__0`, `__1`, … | `http://localhost:5173`, `http://localhost:3000` | Frontend origins allowed to call the API. |

## Code structure

```
src/BreweryCrawl.Api/
  Program.cs                   Setup and middleware order
  Features/Breweries/          Endpoints, errors and data access for breweries
  Infrastructure/
    ErrorHandling/             Error definitions, Result<T>, ProblemDetails setup, global exception handler
    Validation/                Validation with error codes per field
    Chaos/                     Fault injection
    OpenBreweryDb/             HTTP client with resilience settings, plus the snapshot
    Logging/                   Serilog setup
tests/BreweryCrawl.Api.Tests/  Integration tests for the error contract
```

**Expected errors** (not found, validation, business rules) are returned as values (`Result<T>`), not thrown,
so they are visible in method signatures. **Unexpected failures** (bugs, a dependency that is down) are thrown and turned
into ProblemDetails by `GlobalExceptionHandler`.

Validation uses a small custom filter instead of .NET 10's built-in validation, because the built-in version only
returns messages, not the `errorCodes` that a frontend needs to translate them.
