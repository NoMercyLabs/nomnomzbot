# Contributing to NomNomzBot

Thank you for helping. This guide covers how to report a problem, how to propose a change, and what a
pull request needs before it can be merged. It applies to people and to bots in the same way.

- Found a security problem? **Do not open an issue.** Follow [SECURITY.md](SECURITY.md).
- Have a question? Open a [blank issue](https://github.com/NoMercyLabs/nomnomzbot/issues/new/choose) with the `type/question` label.

## Filing an issue

Use one of the issue forms. Each form asks for exactly what a fix needs, so nobody has to come back to
you with questions.

| Form | Use it when |
|------|-------------|
| **Bug report** | Something does not work as it should. |
| **Feature request** | You want the bot to do something new. |
| **Platform API gap** | The bot does not support a method or event of an API it integrates (Twitch, Kick, YouTube, X, Spotify, Discord). |

A good report has:

- **One problem per issue.** Two bugs are two issues.
- **Exact steps.** "Type `!sr never gonna give you up` in chat as a viewer", not "song requests are broken".
- **Expected and actual.** What should happen, and what happened instead.
- **The build.** Open `GET /health/version` on your server and paste the version.
- **Logs**, if you have them. Remove tokens, keys and passwords first.

### Filing as a bot

Bots and scripts file issues through the API (for example `gh issue create`). Use the same headings as
the matching form, in the same order, each as a `### ` heading. End the body with the form's
machine-readable block, filled in, in a fenced `yaml` code block. Each form has its own keys; this is the
block for a bug:

```yaml
kind: bug                # bug | feature | api-gap
area: commands           # see the "Area" list in the forms
platform: twitch         # twitch | kick | youtube | x | none
surface: chat            # chat | dashboard-web | dashboard-desktop | api | overlay | server
build: 0.1.0+<sha>       # from GET /health/version, or "unknown"
channel: <login>         # the channel it happened in, or "none"
```

Apply the labels the form would apply (`type/bug` or `type/feature`, plus `needs-triage`). A bot that
reports the same problem again should comment on the open issue, not open a new one.

## Making a change

For a small fix, open a pull request. For a larger change, open an issue first, so we can agree on the
approach before you spend time on it.

1. Fork the repository and branch from `master` (there is no `main`): `feat/<short-name>` or
   `fix/<short-name>`.
2. Make one small, complete change: everything one behavior needs, end to end, with its tests.
   An endpoint is the controller action, the service method, the data access and the tests together.
3. Run the checks below. All must pass.
4. Open a pull request against `master` and fill in the template.

To run the bot locally, see [Getting started](README.md#getting-started) and
[Development](README.md#development) in the README.

### Project layout

```
server/   .NET 10 backend: Domain → Application → Infrastructure → Api, plus tests
app/      Kotlin Multiplatform + Compose dashboard (desktop and web/Wasm)
scripts/  Helper scripts (PowerShell 7, runs on every OS)
```

## Checks before you push

**Backend** (from `server/`):

```bash
dotnet tool restore
dotnet build NomNomzBot.slnx       # warnings are errors
dotnet test
dotnet csharpier format .
dotnet csharpier check .
```

**Dashboard** (from `app/`):

```bash
./gradlew :composeApp:jvmTest
./gradlew :composeApp:compileKotlinWasmJs   # jvmTest cannot catch a web-only break
```

CI runs the same checks on every push.

## Rules for code

### Backend

- Follow `.editorconfig`. Write the type of every local variable: `var` is a build error (IDE0008).
  The one exception is an anonymous type.
- File-scoped namespaces, nullable enabled, async all the way (never `.Result` or `.Wait()`).
- Return `Result<T>` for operations that can fail, not exceptions or `null`.
- Call services through their interfaces from DI. Do not add MediatR.
- Keep controllers thin. Logic lives in Application and Infrastructure services.
- Put a file next to its siblings in its domain folder. No `utils` or `helpers` folders.
- **Database changes** need a migration in **both** sets: Postgres
  (`server/src/NomNomzBot.Infrastructure/Platform/Persistence/Migrations`) and SQLite
  (`server/src/NomNomzBot.Migrations.Sqlite`). A change in only one set breaks the other database.
- **API changes** must update `server/openapi/v1.json` with `scripts/refresh-openapi.ps1`. Do not edit
  that file by hand. The dashboard tests (`ApiContractTest`, `ApiRouteContractTest`) fail when the
  snapshot and the code disagree.

### Dashboard

- Follow the design system in `.claude/docs/design/spec/frontend-design-system.md`. Use its tokens and
  components. Never hard-code a color or a size.
- One primary action per group. Other buttons are outline or ghost.
- All visible text comes from string resources, in English (`values/strings.xml`) and Dutch
  (`values-nl/strings.xml`). Never put text in code.
- Show only what the bot really does. A setting that has no effect yet must say so.

### Every file

- Every new source file starts with the license header. Copy it from any existing file.
  Use `//` for C#, Kotlin, TypeScript and JavaScript, and `#` for PowerShell, shell, YAML and Docker files.
- Leave no temporary files in the tree.

## Tests

A test must prove behavior. Check what changed after the action: the stored state, the events sent and
the shape of the data. A test that only checks "it returned something" or "it did not throw" is not
accepted. Ask yourself: if this behavior broke, would this test fail?

## Commits

- Use conventional commit messages: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, with a
  scope where it helps (`fix(commands): ...`). Say what changed for the user.
- Do not add `Co-Authored-By` or other attribution trailers. A hook rejects them. Turn it on once per
  clone: `git config core.hooksPath .githooks`.

## License

NomNomzBot is licensed under the [GNU Affero General Public License v3.0 or later](server/LICENSE).
By contributing, you agree that your contribution is licensed under the same terms.
