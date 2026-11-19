# Rolehound

Private web app that finds Brazilian job postings, ranks them against each
user's profile, and helps them apply with AI that never invents facts.

> Status: early development. The scoring engine and the application skeleton
> exist; features arrive one vertical slice at a time.

## Repository layout

```text
src/
  Rolehound.Api/          ASP.NET Core host (Minimal APIs)
    Features/             one folder per module, one subfolder per use case
    Infrastructure/       persistence, HTTP defaults, email, security
  Rolehound.Scoring/      pure scoring rules, no I/O
tests/                    one test project per source project
web/                      React + TypeScript (Vite)
  src/app/                providers and routes
  src/features/           one folder per module, mirroring the API
  src/lib/                shared client code (HTTP)
  src/styles/             design tokens and base styles
tools/parity/             exports the parity dataset from the previous tool
compose.yaml              local PostgreSQL and Mailpit
```

## Development

Requires the .NET 10 SDK, Node.js 26 and Docker.

```bash
docker compose up -d            # PostgreSQL on 5432, Mailpit UI on http://localhost:8025
dotnet run --project src/Rolehound.Api    # API on http://localhost:5080
cd web && npm ci && npm run dev           # web app on http://localhost:5173
```

Checks run by CI on every push:

```bash
dotnet format --verify-no-changes
dotnet test
cd web && npm run lint && npm run build
```

Parity tests against the previous tool need a dataset that is not part of
this repository; see `tools/parity/export_previous_tool.py`. Without it they
are skipped.
