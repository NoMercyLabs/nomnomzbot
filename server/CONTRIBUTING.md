# Contributing to NomNomzBot

## Getting started

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/your-feature` (or `fix/...`)
3. Make your changes in small, complete vertical slices
4. From `server/`: run `dotnet build`, `dotnet test`, and `dotnet csharpier check .` — all three must
   be green (warnings are errors, including the NuGet vulnerability audit)
5. Push and open a PR against `master` (the default branch — there is no `main`)

All commands are identical on every OS.

## Code style

- Follow the `.editorconfig` rules — it enforces the house style, including **explicit types
  everywhere** (`var` is a build error, IDE0008)
- File-scoped namespaces, nullable enabled, async all the way (never `.Result`/`.Wait()`)
- `Result<T>` over exceptions/null for operations that can fail
- No MediatR — use direct service interfaces registered in DI
- Keep controllers thin; logic lives in Application/Infrastructure services
- Every new source file starts with the AGPL license header (see any existing `.cs` file)
- Format with CSharpier before committing: `dotnet csharpier format .` from `server/`

## Testing

- All PRs must pass CI
- Tests must prove behavior — assert state changes, emitted events, and data shapes, not just
  "didn't throw". Smoke-only tests are not accepted
- `dotnet test` runs the full suite locally with no external services required

## Commit messages

Use conventional commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`).
Never include a `Co-Authored-By` trailer.
