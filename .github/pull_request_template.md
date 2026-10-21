## What changes for the user

<!-- One or two sentences. Link the issue: "Fixes #123". -->

## How it was proven

<!-- The tests you added and what each one checks: the state change, the events sent, the shape of the data.
     For a dashboard change, add a screenshot. -->

## Checklist

- [ ] One complete change, with its tests. No unrelated edits.
- [ ] Backend: `dotnet build NomNomzBot.slnx` has no warnings, `dotnet test` passes, `dotnet csharpier check .` passes.
- [ ] Dashboard: `npm run type-check`, `npm run lint`, `npx vitest run` and `npm run build-only` pass in `dashboard/`.
- [ ] Database change: a migration in **both** sets (Postgres and SQLite), or no schema change.
- [ ] API change: `server/openapi/v1.json` refreshed with `scripts/refresh-openapi.ps1`, or no API change.
- [ ] New source files start with the license header.
- [ ] Visible text is in the i18n files, in English and Dutch.
- [ ] No `var` in C#, no hard-coded colors or sizes in the dashboard.
- [ ] No attribution trailers in commit messages.
