# Benchmark: clone check for S-GATE-DRY

Method: docs of each tool fetched 2026-10-11, and the tools run live on this tree the same day (worktree of master b51643bc7). Goal from the plan entry S-GATE-DRY: find a copied block of 15 or more lines in the slice's touched C# and Kotlin files against the whole tree, name both places, and run on Windows in scripts/slice-check.ps1 and the app gate.

Live runs (all on Windows 10, Node 22.14.0, Java 21.0.5):
- jscpd 5.4.1 (npm install into a scratch folder). Probe: a new untracked file holding 25 lines copied from an existing C# file. `jscpd --changed --min-lines 15 --min-tokens 50 --format csharp server/src ZzCloneProbe.cs` exit 1, 13 s, output: `Clone found (csharp) [NEW]`, `ZzCloneProbe.cs [1:5 - 25:65] (25 lines, 94 tokens)` and `NomNomzBot.Infrastructure\Platform\Eventing\TwitchEventSubHostedService.cs [61:5 - 85:65]`, `Found 1 clones (1 new).` The probe file was deleted after the run.
- jscpd plain run, same limits, Migrations ignored: C# 2471 files, 443 clones, 8378 duplicated lines (2.51 percent), 0.6 s scan; Kotlin 907 files, 509 clones, 10381 duplicated lines (4.35 percent), 0.8 s scan. So a gate with no baseline fails on day one.
- PMD CPD 7.28.0 (latest release 2026-09-25, from the GitHub releases API). C# whole tree: 38 files skipped with `Lexical error` (raw string literals and `$$` strings), then `java.lang.OutOfMemoryError: Java heap space` at the default heap and again with `PMD_JAVA_OPTS=-Xmx6g` (whether the variable took effect: not checked). No report. Kotlin whole tree with `-Xmx4g`, `--minimum-tokens 80`: exit 4, 32.6 s, 1131 duplications, 0 files skipped, report is the whole tree (no filter to touched files).
- ReSharper CLI: `dotnet jb --help` (pinned 2026.2.1) lists only `inspectcode` and `cleanupcode`; `dotnet jb dupfinder --help` prints `Command dupfinder is not found`.
- detekt and SonarScanner: docs only. Neither was run (detekt is not in app/build.gradle.kts or the version catalog, checked by grep; SonarScanner needs a server in every documented example).

## Products

- jscpd 5.4.1 https://github.com/kucherenko/jscpd
- PMD CPD 7.28.0 https://pmd.github.io/pmd/pmd_userdocs_cpd.html
- ReSharper command line tools 2026.2.1 https://www.jetbrains.com/help/resharper/ReSharper_Command_Line_Tools.html
- detekt 2.0.0-alpha.6 https://detekt.dev/docs/intro
- SonarScanner CLI with SonarQube Server https://docs.sonarsource.com/sonarqube-server/latest/analyzing-source-code/scanners/sonarscanner/

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Find exact copied blocks in C# | jscpd, PMD CPD, SonarScanner | https://github.com/kucherenko/jscpd | no clone check in scripts/slice-check.ps1 (gates only CodeRedundancy and LanguageUsage, lines 277-282) | missing | S-GATE-DRY |
| Find exact copied blocks in Kotlin | jscpd, PMD CPD, SonarScanner | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | app has no lint and no clone check | missing | S-GATE-DRY |
| Set the floor in lines (we need 15 lines) | jscpd | https://github.com/kucherenko/jscpd | no clone check; jscpd 5.4.1 help lists `-l, --min-lines` | missing | S-GATE-DRY |
| Set the floor in tokens only (no line option) | PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | no clone check; PMD offers only `--minimum-tokens` | missing | S-GATE-DRY |
| Floor fixed by the product (100 tokens over 10 lines) | SonarScanner | https://docs.sonarsource.com/sonarqube-server/latest/user-guide/code-metrics/metrics-definition/ | no clone check; cannot be set to 15 lines | missing | S-GATE-DRY |
| Report only clones that touch changed files, scan the whole tree | jscpd | https://github.com/kucherenko/jscpd | no clone check; jscpd `--changed` proven live: probe copy named both places in 13 s | missing | S-GATE-DRY |
| Name both places with file and line range | jscpd, PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | no clone check; both tools print both files and lines in the live runs | missing | S-GATE-DRY |
| Fail the gate on new clones only (baseline) | jscpd | https://github.com/kucherenko/jscpd | no clone check; today the tree holds 443 C# and 509 Kotlin clones at 15 lines | missing | S-GATE-DRY |
| Baseline file rebuilt when HEAD moves | jscpd | https://github.com/kucherenko/jscpd | none; jscpd wrote .jscpd-baseline.json at the repo root, so it needs a gitignore line | missing | S-GATE-DRY |
| Exit code on duplicates | jscpd, PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | Invoke-Native in scripts/slice-check.ps1:44 judges exit codes | partial | S-GATE-DRY |
| Machine-readable report (json, sarif) | jscpd, PMD CPD, ReSharper inspectcode | https://github.com/kucherenko/jscpd | slice-check.ps1:265 reads the ReSharper XML report | partial | S-GATE-DRY |
| Runs on Windows from PowerShell | jscpd, PMD CPD, ReSharper CLI | https://github.com/kucherenko/jscpd | slice-check.ps1 already runs dotnet tools and gradlew.bat on Windows | partial | S-GATE-DRY |
| C# 14 raw string literals parse | jscpd | https://github.com/kucherenko/jscpd | jscpd scanned 2471 C# files and printed no error in the live run (skips not checked file by file) | missing | S-GATE-DRY |
| C# raw string literals fail the lexer (38 files skipped) | PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | live run: 38 files skipped with Lexical error; a gate that skips files cannot find a copy of them | not-doing | PMD skips 38 C# files in the live run, so the gate would miss copies in them |
| Memory use on the full C# tree | PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | live run: OutOfMemoryError at the default heap and at -Xmx6g | not-doing | PMD ran out of memory on the C# tree in two runs, so it cannot gate the server |
| Restrict the report to touched files | PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | live Kotlin run printed 1131 whole-tree duplications; the gate must filter the output itself | missing | S-GATE-DRY |
| Ignore generated folders (Migrations, bin, obj) | jscpd, PMD CPD | https://github.com/kucherenko/jscpd | no clone check; jscpd `--ignore` globs used in the live run; PMD has `--exclude` | missing | S-GATE-DRY |
| Respect .gitignore by default | jscpd | https://github.com/kucherenko/jscpd | jscpd 5.4.1 help lists `--no-gitignore`; the live run used it | missing | S-GATE-DRY |
| Fail when nothing was scanned | jscpd | https://github.com/kucherenko/jscpd | no clone check; jscpd has `--fail-on-empty` | missing | S-GATE-DRY |
| Ignore renamed identifiers and literals (type 2 clones) | jscpd, PMD CPD | https://pmd.github.io/pmd/pmd_userdocs_cpd.html | no clone check; PMD lists no Kotlin support for these flags | missing | S-GATE-DRY |
| Semantic clones with an embedding model | jscpd | https://github.com/kucherenko/jscpd | not wanted; the model download is 548 MB per jscpd 5.4.1 help | not-doing | The gate needs copied blocks only, and a 548 MB model download is too heavy |
| Dead code, complexity and health score in the same tool | jscpd | https://github.com/kucherenko/jscpd | dead-code is the job of S-GATE-YAGNI | not-doing | S-GATE-YAGNI owns unused code; this gate does one job |
| Duplicate finder in ReSharper CLI | ReSharper CLI | https://blog.jetbrains.com/dotnet/2021/08/12/sunsetting-dupfinder-command-line-tool/ | dotnet jb dupfinder: Command dupfinder is not found | not-doing | JetBrains removed dupFinder from release 2021.3; pinned 2026.2.1 has none |
| Clone inspection inside jb inspectcode | ReSharper CLI | https://www.jetbrains.com/help/resharper/InspectCode.html | gate reads CodeRedundancy and LanguageUsage only; the inspection list page was not fetched (404), so this row is not checked | not-doing | The CLI help lists no duplicate command and the jb report categories gated today hold no clone check |
| Clone detection rule in detekt | detekt | https://detekt.dev/docs/rules/complexity | none; the complexity page lists 15 rules and only StringLiteralDuplication is close | not-doing | detekt has no clone rule; StringLiteralDuplication finds repeated strings only |
| Server-side duplication metrics and dashboard | SonarScanner | https://docs.sonarsource.com/sonarqube-server/latest/analyzing-source-code/scanners/sonarscanner/ | none; every documented example passes sonar.host.url and a token | not-doing | Needs a SonarQube server and Java 21, and the 10-line floor cannot be set to 15 |
| Runtime and install cost | jscpd, PMD CPD | https://github.com/kucherenko/jscpd | jscpd needs Node (22.14.0 here, CI not checked); PMD needs Java 21 (here) and a 4 GB heap | partial | S-GATE-DRY |

## Result

Ledger gate: `python scripts/check-benchmark-ledger.py` printed `check-benchmark-ledger: 0 problem(s)`.

Only jscpd met every part of the goal in a live run. PMD CPD works for Kotlin only. ReSharper CLI and detekt have no clone finder. SonarScanner needs a server.

Open points for the slice (not checked): Node on the CI runner; the cost of `--changed` when HEAD moves (13 s for C#, about 2 s for Kotlin here); the right token floor that matches 15 lines (the live run used 50 tokens with a 15 line floor).

Proposal, not settled: pick jscpd 5.4.1 with `--changed --min-lines 15`, because it alone named both places, filtered to the touched files against the whole tree, parsed all C# and Kotlin files, and ran on Windows in the live runs.
