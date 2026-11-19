# Rolehound

Private web app that finds Brazilian job postings, ranks them against each
user's profile, and helps them apply with AI that never invents facts.

> Status: early development. Only the scoring engine exists.

## Development

Requires the .NET 10 SDK.

```bash
dotnet test
```

Parity tests against the previous tool need a dataset that is not part of
this repository; see `tools/parity/export_previous_tool.py`. Without it they
are skipped.
