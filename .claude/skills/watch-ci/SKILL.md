---
name: watch-ci
description: Push to origin/master and block on the CI verdict, or watch an existing run and report why it went red. Use when asked to push, to check CI, to watch a run, or to find out whether master is green.
---

# Push and watch CI

**A push is not done until CI is green.** Never end a turn with "CI will probably pass" or
"I'll check later". Master never stays red.

Pushing is a **milestone** decision, not a per-commit habit — batch commits locally and push
when a body of work is done. Confirm before pushing if you were not told to.

## The command

```powershell
scripts/push-and-watch.ps1              # push HEAD:master, watch, auto-retry one flake
scripts/push-and-watch.ps1 -NoRetry     # a red is a red
scripts/push-and-watch.ps1 -DryRun      # watch the run for the current HEAD without pushing
```

Exit code **is** the verdict: `0` green, `1` red. On red it prints the failing jobs and the
first error lines, so the next step is diagnosis rather than more `gh` incantations.

It polls for the run **by SHA**, not "the latest run" — the latest run can be someone else's
push, and the run for a fresh commit does not exist instantly.

## By hand, if you must

```bash
gh run list --limit 20 --json databaseId,headSha   # match YOUR sha
gh run watch <run-id> --exit-status
gh run view <run-id> --log-failed
```

## Reading a red

- **No run created ≠ a failed run.** Actions can be degraded or the trigger dropped. The commit
  is pushed and will build when Actions recovers. Say which of the two it is — never report a
  missing run as a red build, and never as a deploy.
- **Known flake**, non-reproducing: the Application suite (~5%). (The SQLite concurrent-writer soak
  was replaced by a deterministic lock test on 2026-09-30.) One re-run is allowed and the script
  says loudly when it retried. **A re-run is not a fix** — a test that "only fails in CI" must not quietly become
  normal.
- Anything else: diagnose, fix, commit, push, watch again — before starting anything else.

## Green CI is not a deploy

CI passing means the image built. It does not mean the box is running it. To finish the job see
`deploy-and-verify`.

## Report back

Verdict (green / red / no-run-created), the run id and URL, and on red the failing job plus the
first real error with file and line. If a flake re-run happened, say so explicitly.
