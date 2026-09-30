<!--
  Copyright (c) NoMercy Labs.
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Playbooks

One skill per repeatable job. Each wraps the real script in `scripts/` rather than re-deriving
the steps by hand — which is exactly where a step gets skipped. Invoke by name with the Skill
tool; they are written to be handed to a subagent verbatim.

| Skill | The job | Wraps |
|---|---|---|
| `build-server` | build + test the backend; is my slice green, is the tree green, will the migration survive an upgrade | `slice-check.ps1` · `verify-tree.ps1` · `migration-check.ps1` |
| `build-app` | build + test the KMP/Compose dashboard, both targets | Gradle `jvmTest` + `compileKotlinWasmJs` |
| `run-the-stack` | start/verify/stop the API + dashboard, prove it in the rendered client | `dev-api.ps1` · `refresh-openapi.ps1` · `mint-jwt.py` |
| `commit-a-slice` | gate, stage by pathspec, sign off, close the tracker entry | `close-slice.ps1` |
| `watch-ci` | push and block on the verdict; read a red | `push-and-watch.ps1` |
| `deploy-and-verify` | ship on green, blue/green switchover, prove the box runs the intended SHA | `ship.ps1` · `switchover.ps1` |
| `devbox` | the containerised work environment, three ways in | `devbox/` |
| `dispatch-a-builder` | brief a subagent, and judge its report | `land-worktree.ps1` |
| `sleak` | UI craft bar for anything visual | — |

Order for a normal piece of work:

```
build-server / build-app  →  run-the-stack  →  commit-a-slice  →  watch-ci  →  deploy-and-verify
```

`.claude/` is gitignored — these files need `git add -f`.
