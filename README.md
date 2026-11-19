# Rolehound

Private web app that finds Brazilian job postings, ranks them against each
user's profile, and helps them apply with AI that never invents facts.

> Status: early development. The scoring engine and the application skeleton
> exist; features arrive one vertical slice at a time.

## Repository layout

```text
backend/                      .NET 10
  Rolehound.slnx              solution
  src/Rolehound.Api/          ASP.NET Core host (Minimal APIs)
    Features/                 one folder per module, one subfolder per use case
    Infrastructure/           persistence, HTTP defaults, email, security
  src/Rolehound.Scoring/      pure scoring rules, no I/O
  tests/                      one test project per source project
  tools/parity/               exports the parity dataset from the previous tool
frontend/                     React + TypeScript (Vite)
  src/app/                    providers and routes
  src/features/               one folder per module, mirroring the API
  src/lib/                    shared client code (HTTP)
  src/styles/                 design tokens and base styles
compose.yaml                  local PostgreSQL and Mailpit
```

## Development

Requires the .NET 10 SDK, Node.js 26 and PostgreSQL (Docker or a local install).

```bash
docker compose up -d        # PostgreSQL on 5432 and Mailpit on http://localhost:8025
```

Without Docker, create the role and databases on a local PostgreSQL:

```bash
psql -d postgres -c "CREATE ROLE rolehound LOGIN PASSWORD 'rolehound' CREATEDB"
createdb -O rolehound rolehound && createdb -O rolehound rolehound_test
```

Then:

```bash
cd backend && dotnet run --project src/Rolehound.Api   # API on http://localhost:5080, migrates on start
cd frontend && npm ci && npm run dev                   # web app on http://localhost:5173
```

In development the first admin comes from `Admin:Email` and `Admin:Username`
(see `appsettings.Development.json`, or override with `dotnet user-secrets`).
Without an SMTP server, emails (with their links) are written to the API log.

Checks run by CI on every push:

```bash
cd backend && dotnet format --verify-no-changes && dotnet test   # API tests need the rolehound_test database
cd frontend && npm run lint && npm run build
```

Parity tests against the previous tool need a dataset that is not part of
this repository; see `backend/tools/parity/export_previous_tool.py`. Without
it they are skipped.
