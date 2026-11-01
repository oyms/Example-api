# Brewery Crawl API

Example backend for a workshop on error handling between backend and frontend.
Built with .NET 10 Minimal APIs. Every error is returned as [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457).

Brewery data comes from [Open Brewery DB](https://www.openbrewerydb.org/), with a bundled snapshot as fallback.
Users can plan **pub crawls**: a list of brewery stops in one city on one date, stored in SQLite.

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

## The API

| Endpoint | |
|---|---|
| `GET /api/breweries?city=&name=&type=&page=&perPage=` | Search breweries |
| `GET /api/breweries/{id}` | One brewery |
| `GET /api/crawls?city=` | All crawls |
| `GET /api/crawls/{id}` | One crawl. The `ETag` header holds its version. |
| `POST /api/crawls` | Create a crawl: `{ "name": "...", "city": "Portland", "date": "2030-06-01" }` |
| `PUT /api/crawls/{id}` | Change name and date. Send `If-Match: "<etag>"` to avoid overwriting someone else's change. |
| `DELETE /api/crawls/{id}` | Delete a crawl |
| `POST /api/crawls/{id}/stops` | Add a stop: `{ "breweryId": "..." }` |
| `DELETE /api/crawls/{id}/stops/{breweryId}` | Remove a stop |
| `GET /errors`, `GET /errors/{code}` | Every error code the API can return |
| `POST /admin/reset` | Reset all crawls to the example data |

### Users (fake auth)

Changing data requires a user. There are no passwords: send the `X-User` header with any name, e.g. `X-User: alice`
(case-insensitive).
The example data has crawls owned by `alice` and `bob`, in Portland. `alice` also has a crawl in the past.

This is for the workshop only. A real API would use e.g. JWT bearer tokens, but the error handling (401, 403) is the same.

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
| 401 | `auth.unauthenticated` | `POST /api/crawls` without `X-User` |
| 403 | `crawl.not_owner` | `X-User: carol`, then change one of alice's crawls |
| 404 | `brewery.not_found` | `GET /api/breweries/does-not-exist` |
| 404 | `crawl.not_found` | `GET /api/crawls/00000000-0000-0000-0000-000000000000` |
| 404 | `crawl.stop_not_found` | Remove a stop that isn't on the crawl |
| 404 | `request.not_found` | `GET /api/nope` |
| 405 | `request.method_not_allowed` | `DELETE /api/breweries` |
| 409 | `crawl.duplicate_stop` | Add the same brewery to a crawl twice |
| 409 | `request.concurrency_conflict` | Two requests change the same crawl at exactly the same time |
| 412 | `crawl.version_mismatch` | `PUT` a crawl with an old `If-Match` ETag |
| 415 | `http.415` | `POST /api/crawls` with `Content-Type: text/plain` |
| 422 | `crawl.too_many_stops` | Add an 11th stop |
| 422 | `crawl.brewery_wrong_city` | Add a Seattle brewery to a Portland crawl |
| 422 | `crawl.brewery_not_open` | Add a brewery with type `closed` or `planning` |
| 422 | `crawl.unknown_brewery` | Add a stop with a brewery id that doesn't exist |
| 422 | `crawl.in_the_past` | Change alice's "Last Summer's Crawl" |
| 429 | `request.rate_limited` | Create more than 3 crawls within a minute (see the `Retry-After` header) |
| 500 | `server.unexpected` | `X-Chaos: fail` |
| 502 | `upstream.unavailable` | `X-Chaos: upstream-down,no-fallback` |
| 503 | `upstream.circuit_open` | `X-Chaos: upstream-circuit-open,no-fallback` |
| 504 | `upstream.timeout` | `X-Chaos: upstream-timeout,no-fallback` (takes about 6 s) |
| 499 | *(no body)* | `X-Chaos: slow`, then abort the request in the client |

Some choices that are worth discussing:

- **400 or 422?** 400 means the request itself is invalid (missing field, wrong format). 422 means it is valid, but breaks a business rule.
- **404 or 422 for an unknown brewery?** When the brewery id is in the URL, the resource is missing: 404.
  When it is in the request body, the request refers to something that doesn't exist: 422.
- **403 or 404 for someone else's crawl?** Crawls are public, so 403 reveals nothing. For private data, 404 hides that it exists.
- **409 or 412?** 412 is the client's own check (`If-Match`) failing. 409 is a conflict with the current state.
- **No `If-Match`?** Then the API doesn't check the version, and the last write wins. Sending it is up to the client.

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

## Reading the logs

Every request gets one summary line, including the error code and user:

```
[21:37:15 WRN] 3c2f… Serilog.AspNetCore.RequestLoggingMiddleware
      HTTP POST /api/crawls/2979…/stops responded 403 in 17 ms (user: carol) crawl.not_owner
```

The hex id after the level is the trace id, which is the same as `traceId` in the error response.
Search for it to find everything that happened during that request.
With `Logging__Format=json`, `ErrorCode`, `User` and `TraceId` are separate fields you can filter on.

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
| `ConnectionStrings__Crawls` | `crawls.db` in the app folder | Where the SQLite database is stored. Delete the file or call `POST /admin/reset` to start over. In Docker it is lost when the container is removed. |
| `RateLimiting__CreateCrawl__PermitLimit` | `3` | Crawls each user can create per minute. |
| `Cors__AllowedOrigins__0`, `__1`, … | `http://localhost:5173`, `http://localhost:3000` | Frontend origins allowed to call the API. |

## Code structure

```
src/BreweryCrawl.Api/
  Program.cs                   Setup and middleware order
  Features/Breweries/          Endpoints, errors and data access for breweries
  Features/Crawls/             Crawl entity with business rules, service, endpoints and errors
  Infrastructure/
    Auth/                      Fake X-User authentication
    Persistence/               EF Core with SQLite, example data
    RateLimiting/              Rate limit for creating crawls
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
