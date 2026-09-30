---
name: commit-a-slice
description: Commit a finished vertical slice in the NomNomzBot repo the safe way — gate, stage by explicit pathspec, sign off, close the tracker entry. Use when work is validated and ready to commit, especially on a tree shared with other agents.
---

# Commit a slice

A slice is the smallest change that delivers a **working, testable piece of the full data flow**
— controller + service + data access + test, not "a controller here, a model there". Finish and
validate one before starting the next. A large uncommitted changed-file count is a failure
state, not progress.

## The sequence

1. **Gate it.** Backend → `scripts/slice-check.ps1` (see `build-server`). Frontend →
   `jvmTest` **and** `compileKotlinWasmJs` (see `build-app`). Never commit on red.
2. **Stage by explicit pathspec.** The tree is shared with other agents.

   ```powershell
   git commit -s --only -m @'
   feat(moderation): heat threshold enforces on timeout

   Longer body if it earns its place.
   '@ -- server/src/.../Thing.cs server/tests/.../ThingTests.cs
   ```

   - Use the **PowerShell tool**, with a single-quoted here-string. The closing `'@` must be at
     column 0.
   - Judge the result by `$LASTEXITCODE`. **Never `2>&1`** — git writes normal progress to
     stderr and under `ErrorActionPreference = Stop` that becomes a terminating error on a
     *successful* command.
   - **Always `-s`** (sign off). **Never `Co-Authored-By`** — not ever.
   - **Never `git stash`** on a shared tree. To verify around someone else's broken WIP, use
     `slice-check.ps1 -AtCommit <sha>` in a throwaway worktree.
3. **The track split is suspended during the remediation campaign (D7); one slice may span
   `server/` and `app/`.** Keep a contract and its consumer in the same commit
   (`openapi/v1.json` + Kotlin DTOs).
4. **Close the tracker entry** — the tracker holds **remaining work only**, so a shipped slice
   is *deleted*, never annotated as done:

   ```powershell
   scripts/close-slice.ps1 -Slice S006 -Message "live-game money refunds on settle failure"
   scripts/close-slice.ps1 -Slice S006 -Message "..." -Follow @("- **S006b** Follow-up the slice exposed.", "  Done-when: ...")
   ```

   `close-slice.ps1` matches the literal bold id at the start of a bullet; pass the full id.

## Conventions

- Conventional messages: `feat:`, `fix:`, `chore:`, `docs:`. Subject says what the code now
  **does**, not what you touched.
- Branch is `master` (never `main`). `origin` = `NoMercyLabs/nomnomzbot`.
- `.claude/` is gitignored — spec and skill files need `git add -f`.
- Batch commits locally. **Do not push per commit** — pushing is a milestone decision, and CI
  runs are not free. See the `watch-ci` skill (`scripts/push-and-watch.ps1`).

## Report back

The commit sha, the subject line, and the exact file list committed. If anything was left
uncommitted in the tree, name it and say why.
