# Contributing to NomercyBot

## Getting Started

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes
4. Run tests: `dotnet test`
5. Push and open a PR against `main`

## Code Style

- Follow the `.editorconfig` rules
- File-scoped namespaces
- No MediatR - use direct service interfaces
- Keep controllers thin, logic in Application/Infrastructure services

## Testing

- All PRs must pass CI
- Add tests for new features
- Domain and Application layers should have unit tests
- Infrastructure tests can use a test PostgreSQL database

## Commit Messages

Use conventional commits:
- `feat:` new feature
- `fix:` bug fix
- `refactor:` code change that neither fixes a bug nor adds a feature
- `docs:` documentation only
- `test:` adding or fixing tests
- `chore:` maintenance tasks
