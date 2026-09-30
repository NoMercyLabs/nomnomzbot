# NomNomzBot — AI Assistant Context

An open-source, multi-tenant, multi-platform bot platform — one channel, many platform connections
(Twitch, Kick, YouTube, X Live, simultaneously) managed from one uniform dashboard for streamers,
moderators and viewers. One deployment supports unlimited channels — each streamer gets a full
isolated dashboard, pipeline editor, custom commands, event responses, timers, widgets & overlays,
and integrations (Spotify, Discord, YouTube, TTS).

**Binding product statement:** `.claude/docs/design/PRODUCT-ALIGNMENT.md` (decisions D1–D12, glossary,
domain table) — it wins over any older sentence in this file or any spec.

Licensed under **AGPL-3.0**. Copyright (C) NoMercy Labs.

---

## Critical Rules — Read First

- **Everything is `NomNomzBot.*`** — namespace, folder, assembly, and product all match. Every `.cs` file's `namespace` and every csproj `<RootNamespace>` is `NomNomzBot.*`. (History: the legacy code carried `NoMercyBot.*` from an incomplete earlier rename; the clean-slate rebuild **fully migrated** it to `NomNomzBot.*` via a repo-wide rename — there is no `NoMercyBot` left in code or specs, **do not reintroduce it**. The **copyright holder is still NoMercy Labs** — the company name in license headers is unaffected.)
- **Username is `Stoney_Eagle`** — underscore, not hyphen. Never change this.
- **shadcn/ui (new-york) is the source of truth for the dashboard design** — ported 1:1 to Compose; full spec in `.claude/docs/design/spec/frontend-design-system.md`. The old Figma file (`MkKBuW2Ee6T5jC8fCtZsM0`) is **discarded** (not a viable dashboard); a fresh Figma may be minted from the spec later.
- **HTML mockups** (the `nomnomzbot-design` repo — an external archived repo, not in this tree) are a loose historical reference only — the design system, not the mockups, is authoritative.
- **No `Co-Authored-By` in git commits** — ever. Mechanically enforced by a `commit-msg` hook;
  run `git config core.hooksPath .githooks` once per clone (see *Git Conventions*).
- **No MediatR** — services are called directly via typed interfaces registered in DI.
- **No Roslyn** — don't use Roslyn for code generation or analysis.
- **Don't ask permission to fix bugs** — find it, fix it, move on.
- **No fake/seed data for community** — all community/viewer data must come from the real Twitch API. Never fabricate viewer lists, subscriber counts, etc.
- **Full external-API coverage — implement, never remove.** Every method and every event of every API we integrate (Twitch Helix + EventSub, Spotify, Discord, YouTube, …) gets implemented; a missing one is a gap to ADD. Beta/restricted surfaces ship with graceful degradation, never get skipped or deleted. Never act on a "deprecated/skip" claim about an external API without re-verifying the live docs first.
- **Don't ask "should I continue?" or "want me to fix this?"** — just do it.
- **Match the design system exactly** — the shadcn (new-york) tokens, component catalogue, and variant tables in `frontend-design-system.md`; correct tokens/spacing/variants. Never hardcode a color or `dp`; do not approximate.
- **Load the `sleak` skill before writing any UI, every time.** The design system says which token; Sleak says which thing wins the eye. Its three core rules are non-negotiable on every screen: **one primary action per group** (siblings go outline/ghost, destructive gets its own treatment — a row of equal-weight buttons is a defect), **concentric radius** (a nested rounded container never reuses the parent radius when padding > 0), and **scarce accent** (full-chroma accent marks the single most important task on the page, nothing else). This applies to Claude and to every dispatched agent — writing a screen without loading it has produced shipped defects.
- **Test every interactive element** — never claim something "works" without full validation.
- **No half-assed work** — seed ALL data, test EVERY button, run parallel where possible.
- **Track split + handoff inboxes — SUSPENDED (not allowed to be used) during the remediation campaign (2026-08-22, D7).** Claude works backend + frontend; the work queue is `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md`; the design bar is the Sleak skill + the shadcn catalogue. The original rule ("Check your handoff inbox at session start — `handoff/for-backend.md` / `handoff/for-frontend.md`; open items are picked up automatically") stays on the books below and resumes when the campaign ends.
- **CI green before sign-off** — run the full test suite before EVERY commit; after EVERY push, `gh run watch <run-id> --exit-status` and fix failures immediately. See *CI Gate*.
- **Work queue = `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md`, top to bottom** (D8: stabilize before adding; new feature ideas go to the tracker as ideas).
- **`saas` mode is a RESTRICTED option** — operating NomNomzBot as a hosted service for third parties is against the project license (reserved to NoMercy Labs); self-hosting your own bot is always free. Every doc/script surface that shows saas instructions must carry this restriction marker — never drop it.

---

## Code Quality Bar — Enforce Without Being Asked

This is a long-lived, top-notch project. Write every file with love and care, not speed — this is not a sprint. Apply these on **every** code change, unprompted:

- **Think 3× before writing.** Right *place* for the file? Right *structure* for the function? Right *path forward*? When placement or structure isn't obvious, state the decision and the reason. Never slap a file down "somewhere".
- **Placement by responsibility.** Identify the layer (Domain / Application / Infrastructure / Api / platform SDK) and the domain folder, then put the file beside its siblings. No `misc` / `helpers` / `utils` dumping grounds. Organize by **domain**, never by provenance (no `Generated/` folders).
- **One responsibility per file, class, and function.** Keep functions small and single-purpose. A function that needs a comment to explain *what* it does is usually two functions.
- **DRY — strongly encouraged, not absolute.** Extract genuinely shared logic; but don't unify things that merely look alike — wait for the third occurrence (Rule of Three). Clarity beats premature abstraction.
- **SOLID.** Depend on interfaces (the existing convention), constructor-inject, no god classes, no fat interfaces. Extend via new types, not by growing `switch` statements.
- **No bloat / YAGNI.** No speculative params, abstractions, or "just in case" code. No file bloat — split when a file starts doing more than one thing. Generated code stays generated and thin; hand-written code stays minimal.
- **Match the surrounding code.** File-scoped namespaces, `Nullable` enabled, async all the way (never `.Result`/`.Wait()`), `Result<T>` over exceptions/null, existing naming. Don't introduce a second style.
- **Explicit types — never `var`.** Stoney's house style: spell the type on every local (`List<string> ids = ...`, not `var ids = ...`). The *only* exception is when the type is genuinely unnameable — an anonymous-type projection (`new { ... }` from an EF/LINQ `Select`/`GroupBy`/`ToDictionary`), where C# forces `var`. `.editorconfig` flags `var` as an **error** (IDE0008). This is a hard rule and **must be in every agent brief that writes C#** — it has been silently violated before.
- **License header on every source file you create.** This project is **AGPL-3.0** (`server/LICENSE`); copyright holder is **NoMercy Labs**. At the very top, verbatim:
  ```
  // -----------------------------------------------------------------------------
  //  Copyright (c) NoMercy Labs.
  //
  //  This file is part of NomNomzBot, free software licensed under the GNU Affero
  //  General Public License v3.0 or later. You may redistribute and/or modify it
  //  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
  //
  //  SPDX-License-Identifier: AGPL-3.0-or-later
  // -----------------------------------------------------------------------------
  ```
  Use `//` for C#/TS/JS. For files whose language uses `#` comments (PowerShell, sh, Python, YAML, Dockerfile) use the same text with `#` instead of `//`. Skip files that can't carry comments (plain `.json`). For generated files, `// <auto-generated />` must be the **first** line (compiler/analyzer requirement), with this header directly beneath it. Rider renders it as the file header via `server/.editorconfig` `file_header_template` + `IDE0073`.
- **Never leave a temp file behind.** Scratch downloads, throwaway scripts, report dumps, spec copies — delete them the moment the step that needed them is done, or write them outside the repo tree. The working tree stays clean, always.
- **Format with CSharpier before every commit.** After writing or editing any C#, run `dotnet csharpier format .` from `server/`; `dotnet csharpier check .` must pass before committing — a hard gate alongside build + tests. The formatter is pinned in `server/.config/dotnet-tools.json` (`dotnet tool restore` to install). This applies to code written by dispatched agents too — their briefs must include the format step. Formatting alone is NOT the gate: run `scripts/slice-check.ps1` (next bullet), which does this plus the cleanup and inspection steps.
- **Run the cleanup gate, not just csharpier.** `scripts/slice-check.ps1` (build → test → csharpier → `dotnet format style ... --severity warn`, scoped to the slice's own files) is the ONE command a slice runs before commit — csharpier only formats, it never removes an unused `using` (IDE0005, `server/.editorconfig` S-cleanup block + `GenerateDocumentationFile`/`EnforceCodeStyleInBuild` in `server/Directory.Build.props`). "Redundant nullable suppression" and "merge into pattern" have no Roslyn equivalent, so `dotnet format` cannot see them: the gate runs `jb inspectcode` (ReSharper CLI, pinned in the tool manifest) and FAILS the slice on either, naming file and line. ReSharper detects both but auto-fixes neither (verified 2026-08-25 against `CSFixCodeIssues`, `CSRemoveCodeRedundancies` and the language-usage tasks), so fixing them is still manual — but it is the writing agent's job, caught before commit, never a cleanup pass left to the owner.

### Testing Standard — tests must prove behavior

- **Test data shapes and the consequences of actions, not the surface.** When an action runs, assert the resulting **state change**, the **events/messages emitted**, and the **side effects** — the software actually following through. Verify the *shape* of returned/persisted data (fields, types, invariants), not merely that a call returned non-null or didn't throw.
- **Surface/smoke-only tests are void.** "It returned something", "no exception", a mock asserting it was called — these contribute nothing, give false confidence, and waste time and money. Don't write them; don't count them.
- A test must be able to **fail for the right reason**: if the behavior it describes broke, the test breaks. If it can't, it isn't a test.

---

## Workflow — Vertical Slices, Committed When Validated

Work structured and organized — small, complete vertical slices, not scattered edits. The opposite of "change 10 files, a class here a feature there, commit later."

- **One executable step at a time.** Define the slice's start and finish up front, then build it block by block toward that goal.
- **A slice is the smallest change that delivers a working, testable piece of the full data flow** — end to end. Example: one API endpoint = controller action + service method + data access + test. Not "a controller here, a model there."
- **Commit each validated scope.** When a function / feature / model / endpoint is implemented, tested, and **validated to work to Stoney's standards** (proven, not merely compiling), commit it. One endpoint = one commit (or a few) covering its fully vetted flow. Surface the validation evidence.
- **Never scatter, never pile up.** Finish and validate one slice before starting the next. Don't let the working tree accumulate uncommitted changes — a large changed-file count (e.g. 381) is a failure state, not progress.
- The execution plan (`SHORTCOMINGS-EXECUTION-PLAN.md`) lists remaining work only. A shipped slice is deleted with `scripts/close-slice.ps1`, never annotated DONE/CLOSED/SHIPPED.

---

## Team & Track Ownership — Backend vs Frontend

**SUSPENDED for the remediation campaign (2026-08-22, PRODUCT-ALIGNMENT D7):** Claude works backend
+ frontend in one lane; the `handoff/*.md` files are not used; the design bar is the Sleak skill + the
shadcn catalogue (the designer's own rules). The work queue is `SHORTCOMINGS-EXECUTION-PLAN.md`, top
to bottom. The section below is the **pre-campaign rule**, kept for the record.

Two people, one repo, two strictly separated tracks. Detect the active track from `git config user.name`.

| Track | Person | Owns (commit surface) | Never touches |
|-------|--------|-----------------------|---------------|
| **Backend** | `Stoney_Eagle` | `server/`, root infra (`docker-compose.yml`, `deploy.*`, `.env.example`, `.github/`), `CLAUDE.md`, `.claude/` | `app/` |
| **Frontend** | `aaoa-dev` (designer) | `app/` | `server/`, root infra, `CLAUDE.md`, anything security-sensitive |

- **Commits never cross the boundary.** A backend commit contains no `app/` files; a frontend commit contains no `server/` or root-infra files. If the working tree mixes both, stage and commit only your track's files — leave the rest untouched.
- **Never rewrite the other track's history.** No rebase, amend, force-push, or revert of anything the other track has pushed. Rebasing your **own unpushed** commits onto `origin/master` is fine (`git pull --rebase`); everything already on the remote stays as-is. A conflict inside the other track's files → stop and coordinate via a handoff entry, don't resolve it yourself.
- **`aaoa-dev` does not do backend or security — Claude carries that for him.** On the frontend track: never edit server code, secrets, tokens, OAuth, CORS, JWT, or auth logic on your own initiative. If a task seems to need it, use the boundary override below, and explain the backend/security reasoning to him in plain, non-jargon language so he learns why.
- **Boundary override — only via an explicit yes/no question, never silently.** When work on your track genuinely requires a change on the other track's side, ask the user directly with a yes/no `AskUserQuestion` ("This needs a <backend/frontend> change: <what and why>. Make it now?"). **Yes** → make the change, in its own commit(s), scoped to that need. **No** → write the full findings into the other track's handoff file: what is needed, the exact desired change, and your reasoning — then continue your work without crossing. This is self-enforcing: apply it unprompted, every time — needing a reminder is a failure.
- **The API contract is the only bridge.** The frontend consumes the backend exclusively through the typed shared KMP client and the committed `server/openapi/v1.json` snapshot. Contract changes originate on the backend track; the frontend syncs Kotlin DTOs from the snapshot (`ApiContractTest` guards drift).

### Handoff TODOs — cross-track work orders (pre-campaign rule; unused under D7)

Two committed files (so they travel through git between machines):

- `handoff/for-backend.md` — frontend leaves work for the backend track here
- `handoff/for-frontend.md` — backend leaves work for the frontend track here

Rules:

1. **At session start and before starting new work, read YOUR inbox.** Open items there are picked up automatically — the user does not need to mention them.
2. **Leaving work:** append an entry to the OTHER track's file using the template inside it (date, from, what, why, where, done-when). Commit it together with the work that produced it.
3. **Completing work:** move the entry to the file's **Done** section with the commit hash(es), committed alongside the fix.
4. **Entries must be self-contained** — the reader has no access to your conversation. Name the files, endpoints, and acceptance criteria explicitly.

---

## CI Gate — a push is not done until CI is green

1. **Before EVERY commit: run the tests.** Backend: `scripts/slice-check.ps1` (build + slice tests + csharpier + cleanup + ReSharper inspection); full-tree equivalent is `dotnet test` + `dotnet csharpier check .` from `server/`. Frontend: the Gradle test tasks incl. `jvmTest` (`ApiContractTest`). Never commit on red.
2. **After EVERY push: watch the run and block on it.**
   ```bash
   gh run list --limit 1                 # grab the run id for the pushed commit
   gh run watch <run-id> --exit-status   # block until it finishes
   ```
   Watching is part of the push — never end a turn with "CI will probably pass" or "I'll check later".
3. **CI red → fix it now.** Diagnose, fix, commit, push, watch again — before signing off or starting anything else. `master` never stays red.
4. **Known flake:** the Application test suite fails intermittently (~5%). A lone red that doesn't reproduce locally → re-run the job once before digging.

---

## Known Issues / Current State (as of 2026-09-30)

| Issue | Notes |
|-------|-------|
| EventSub reconnects every ~5 min | Normal Twitch behavior — server sends a `reconnect` message |
| Bot token invalid after key change | `ENCRYPTION_KEY` rotation requires bot re-auth |
| Application test suite rare flake | ~5% intermittent failure; a lone red that won't reproduce locally → re-run once before digging |
| SQLite concurrent-writer contention in the Infrastructure suite | second known non-reproducing red (see `scripts/push-and-watch.ps1` header); a lone red that does not reproduce locally → re-run once |
| EventSub conduit mode is opt-in | `EventSub:Conduits:Enabled` (env `EventSub__Conduits__Enabled`) is false by default after the 2026-09-29 conduit deploy left the bot deaf; per-owner WebSocket sessions carry everything until the blue/green takeover is proven live (tracked in the execution plan). |

---

## Git Conventions

- No `Co-Authored-By` in commits — ever. Enforced mechanically, not just by convention: a
  `commit-msg` hook at `.githooks/commit-msg` rejects any commit whose message carries a
  `Co-Authored-By`/`Claude-Session` trailer (AI sessions have added these anyway, following a
  generic system-level convention that overrides this file's explicit instruction — CLAUDE.md
  alone was not enough). **One-time setup per clone: `git config core.hooksPath .githooks`.**
- Conventional commit messages preferred (`feat:`, `fix:`, `chore:`, etc.)
- Main branch: `master` (never `main`)
- Remotes: `origin` = `NoMercyLabs/nomnomzbot` (canonical, push here); `fork` = personal `StoneyEagle/nomnomzbot`
- Feature branches: `feat/description` or `fix/description`
- All code lives in this monorepo (`server/` backend, `app/` KMP + Compose frontend)
- (Suspended during the remediation campaign — PRODUCT-ALIGNMENT D7; while suspended, one slice may span `server/` and `app/`, and a contract and its consumer land in the same commit.) Backend and frontend are separate tracks — commits never mix `server/` and `app/` files, and neither track rewrites the other's history (see *Team & Track Ownership*)
- Tests pass before every commit; every push is followed by `gh run watch <run-id> --exit-status` (see *CI Gate*)

---

## Reference — read the file when the task touches it

Moved out of this file so every session starts lean; Grimora indexes them (`doc <terms>`).

- Repository Layout: `.claude/docs/reference/repository-layout.md`
- Tech Stack: `.claude/docs/reference/tech-stack.md`
- Backend Architecture: `.claude/docs/reference/backend-architecture.md`
- Frontend Architecture: `.claude/docs/reference/frontend-architecture.md`
- Twitch Integration: `.claude/docs/reference/twitch-integration.md`
- Environment Variables: `.claude/docs/reference/environment-variables.md`
- Common Tasks: `.claude/docs/reference/common-tasks.md`
- Pipeline Engine: `.claude/docs/reference/pipeline-engine.md`
- Design System: `.claude/docs/reference/design-system.md`
- Useful Local Dev URLs: `.claude/docs/reference/local-dev-urls.md`
- First-Time Setup Wizard: `.claude/docs/reference/setup-wizard.md`

### Tooling & precedence

- `_GAP-AUDIT.md` is authoritative over `_READINESS.md`; the execution plan lists remaining work only.
- `FEATURES.md` and `FEATURES-BY-SIDEBAR.md` are the capability inventory.
- Playbooks: `.claude/skills/README.md` (build-server, build-app, run-the-stack, commit-a-slice, watch-ci, deploy-and-verify, dispatch-a-builder, devbox, sleak), and the scripts they wrap: `slice-check`, `verify-tree`, `migration-check`, `push-and-watch`, `close-slice`, `land-worktree`, `refresh-openapi`, `mint-jwt.py`, `ship`, `switchover`.
- Any schema change lands in BOTH migration sets (Postgres: `Infrastructure/Platform/Persistence/Migrations`; SQLite: `NomNomzBot.Migrations.Sqlite`).
- App changes: run `compileKotlinWasmJs`, not only `jvmTest` (`jvmTest` cannot catch a Wasm-only break).
