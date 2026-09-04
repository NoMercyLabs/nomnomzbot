---
name: build-server
description: Build and test the NomNomzBot backend (server/). Use when asked to build the server, run backend tests, check whether HEAD is green, verify one slice before committing, or prove a migration survives an upgrade. Covers slice-check, verify-tree and migration-check.
---

# Build & test the server

Three gates, three different questions. Pick the one that matches the question you were asked;
do not substitute a cheaper one and report it as the expensive one.

| Question | Command |
|---|---|
| Is MY slice green, and is it formatted? | `scripts/slice-check.ps1` |
| Is the WHOLE tree green? | `scripts/verify-tree.ps1` |
| Will this migration survive an UPGRADE? | `scripts/migration-check.ps1` |

All three are PowerShell. Run them with the **PowerShell tool**, from the repo root.

## 1. Slice gate — before every commit

```powershell
scripts/slice-check.ps1 -TestProject tests/NomNomzBot.Api.Tests `
                        -Filter "FullyQualifiedName~SecurityHeaders" `
                        -Paths server/src/.../Thing.cs,server/tests/.../ThingTests.cs
```

Does build → the slice's own tests → **the full unfiltered suite of every project whose layer
`-Paths` touched** → csharpier → `dotnet format style` → `jb inspectcode`, the formatting legs
scoped to `-Paths`. **`jb` detects but never auto-fixes** "redundant nullable suppression" and
"merge into pattern" — fix those by hand; they are yours to fix, not the owner's.

The unfiltered leg is what stops a green slice from turning master red. A filter only proves the
tests *you wrote* pass; it cannot see the test somebody else wrote against the thing you changed.
A seeder gaining a seventh system role passed the filtered gate, was committed, and broke
`IamCatalogSeederTests` — a test in a project that filter never ran. Expect Infrastructure to cost
~7 minutes; that is the price of the commit being safe, and it is far cheaper than a red master.

`-AtCommit <sha>` verifies a committed sha in a throwaway worktree. Use it when another agent's
uncommitted work in the shared tree breaks a file you do not own. **Never `git stash`.**

## 2. Full tree — before accepting work or claiming HEAD is green

```powershell
scripts/verify-tree.ps1                # build + all 4 server suites + csharpier
scripts/verify-tree.ps1 -IncludeApp    # also forces the Kotlin jvmTest suite
scripts/verify-tree.ps1 -AtCommit <sha>
```

A **filtered** run is not evidence the tree is green. The script kills stray testhost/API
processes first (they hold the build DLLs and produce fake compile errors) and rebuilds the test
assemblies explicitly (a stale `--no-build` run once reported 1243 tests where the truth was
4279).

## 3. Migration gate — whenever you add or change a migration

```powershell
scripts/migration-check.ps1                       # SQLite — the DEFAULT self-host runtime
scripts/migration-check.ps1 -Provider Postgres    # needs `docker compose up -d postgres`
scripts/migration-check.ps1 -SeedSql "INSERT ..." # rows the new migration must cope with
```

It migrates to the *previous* migration, seeds rows, then applies the newest one — the upgrade
path no unit test exercises. **There are two migration assemblies** (`NomNomzBot.Migrations.Sqlite`
and `NomNomzBot.Infrastructure`/Npgsql). A migration added to only one of them breaks the other
provider's deployments. Always generate both, and run this gate for both providers.

## Raw commands, when you need them

```powershell
cd server
dotnet build NomNomzBot.slnx -c Debug
dotnet test                                    # all suites
dotnet test tests/NomNomzBot.Domain.Tests      # one suite
dotnet tool restore                            # csharpier 1.3.0, jb 2026.2.1, dotnet-ef 10.0.8
dotnet csharpier check .
```

## House rules the gates cannot check for you

- **Explicit types, never `var`** (`.editorconfig` IDE0008 is an *error*). The only exception is
  an anonymous-type projection, where C# forces it.
- **License header** at the top of every new source file (AGPL-3.0, NoMercy Labs).
- **Warnings are errors** — resolve them, never suppress.
- A test asserts a **state change, an emitted event, or a side effect**. "It returned non-null"
  and "it did not throw" are void and do not count.

## Report back

State the command you ran, the pass/fail, and on failure the **first real error** with its file
and line — not the whole log. If you re-ran anything, say so and why.
