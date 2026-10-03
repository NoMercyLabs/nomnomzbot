#!/usr/bin/env python3
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
"""The ReSharper step of slice-check.ps1, for a shell that cannot run PowerShell (a sandboxed agent).

Runs `dotnet jb inspectcode` on the given .cs files and fails on the same issue families the gate
fails on (CodeRedundancy, LanguageUsage: a redundant `!`, a mergeable pattern, ...). Build first:
it runs with --no-build.

Usage: python scripts/inspect-slice.py server/src/A.cs,server/tests/B.cs
"""

import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
from pathlib import Path

GATED_CATEGORIES = {"CodeRedundancy", "LanguageUsage"}


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    repo = Path(__file__).resolve().parent.parent
    server = repo / "server"
    paths = [p.strip() for p in sys.argv[1].split(",") if p.strip().endswith(".cs")]
    relative = [p[len("server/"):] if p.startswith("server/") else p for p in paths]
    if not relative:
        print("no .cs files to inspect")
        return 0

    report = Path(tempfile.gettempdir()) / "inspect-slice.xml"
    command = [
        "dotnet", "jb", "inspectcode", "NomNomzBot.slnx",
        f"--include={';'.join(relative)}", "--no-build", "--format=Xml",
        f"--output={report}", "--severity=WARNING",
    ]
    if subprocess.run(command, cwd=server, stdout=subprocess.DEVNULL).returncode != 0:
        print("jb inspectcode failed")
        return 1

    root = ElementTree.parse(report).getroot()
    report.unlink(missing_ok=True)
    category_of = {t.get("Id"): t.get("CategoryId") for t in root.iter("IssueType")}
    hits = [i for i in root.iter("Issue") if category_of.get(i.get("TypeId")) in GATED_CATEGORIES]
    for issue in hits:
        print(f"  {issue.get('File')}:{issue.get('Line')} {issue.get('Message')}")
    if hits:
        print(f"ReSharper found {len(hits)} gated issue(s) - fix them before committing")
        return 1
    print("INSPECT OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
