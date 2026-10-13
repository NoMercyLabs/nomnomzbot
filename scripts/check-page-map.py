# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
"""Prove the Vue dashboard page map (decision V23) covers every feature once.

Reads FEATURES-BY-SIDEBAR.md (the feature list), the page map
.claude/docs/design/spec/frontend-vue-page-map.md and server/openapi/v1.json.

Fails (exit 1) when:
  - a feature is missing from the map,
  - a feature is placed more than once,
  - the map names a feature that is not in the feature list,
  - a level is not overview, detail or advanced,
  - an endpoint in the map is not in v1.json (method and path),
  - a row has no endpoint and does not say "no endpoint",
  - a mockup file named in the map does not exist.

Usage: python scripts/check-page-map.py
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FEATURES = ROOT / "FEATURES-BY-SIDEBAR.md"
PAGE_MAP = ROOT / ".claude" / "docs" / "design" / "spec" / "frontend-vue-page-map.md"
OPENAPI = ROOT / "server" / "openapi" / "v1.json"
MOCKUPS = ROOT / ".scratch" / "layout-mockups"

LEVELS = {"overview", "detail", "advanced"}
METHODS = ("get", "post", "put", "patch", "delete")
ENDPOINT = re.compile(r"`(GET|POST|PUT|PATCH|DELETE) (/[^`]*)`")
MOCKUP_FILE = re.compile(r"`?(\d+-[a-z-]+\.html)")


def read_features() -> list[str]:
    names: list[str] = []
    for line in FEATURES.read_text(encoding="utf-8").splitlines():
        match = re.match(r"^\|\s*\*\*(.+?)\*\*\s*\|", line)
        if match:
            names.append(match.group(1).strip())
    return names


def read_endpoints() -> set[tuple[str, str]]:
    spec = json.loads(OPENAPI.read_text(encoding="utf-8"))
    found: set[tuple[str, str]] = set()
    for path, item in spec["paths"].items():
        for method in METHODS:
            if method in item:
                found.add((method.upper(), path))
    return found


def split_cells(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def is_rule_row(cells: list[str]) -> bool:
    return all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells)


def read_map() -> tuple[list[dict], list[str]]:
    rows: list[dict] = []
    problems: list[str] = []
    section = ""
    for number, line in enumerate(PAGE_MAP.read_text(encoding="utf-8").splitlines(), 1):
        if line.startswith("## "):
            section = line[3:].strip()
            continue
        if not line.startswith("|") or not section:
            continue
        cells = split_cells(line)
        if is_rule_row(cells) or cells[0] in ("Feature", "Route"):
            continue
        not_placed = section.lower().startswith("not placed")
        width = 4 if not_placed else 5
        if len(cells) != width:
            problems.append(f"line {number}: {len(cells)} cells, expected {width}: {cells[0]}")
            continue
        rows.append(
            {
                "line": number,
                "section": section,
                "not_placed": not_placed,
                "feature": cells[0],
                "level": "" if not_placed else cells[2].lower(),
                "mockup": "" if not_placed else cells[3],
                "endpoint": cells[-1],
            }
        )
    return rows, problems


def main() -> int:
    features = read_features()
    known = set(features)
    openapi = read_endpoints()
    rows, problems = read_map()

    placed: dict[str, list[str]] = {}
    for row in rows:
        placed.setdefault(row["feature"], []).append(f"{row['section']} (line {row['line']})")

    for name in features:
        if name not in placed:
            problems.append(f"missing from the map: {name}")
    for name, spots in placed.items():
        if len(spots) > 1:
            problems.append(f"placed {len(spots)} times: {name}: " + "; ".join(spots))
        if name not in known:
            problems.append(f"not in FEATURES-BY-SIDEBAR.md: {name}")

    for row in rows:
        where = f"line {row['line']} ({row['feature']})"
        if not row["not_placed"] and row["level"] not in LEVELS:
            problems.append(f"{where}: level '{row['level']}' is not overview, detail or advanced")
        if not row["not_placed"] and MOCKUPS.is_dir():
            file_match = MOCKUP_FILE.search(row["mockup"])
            if not file_match or not (MOCKUPS / file_match.group(1)).is_file():
                problems.append(f"{where}: mockup file not found in the element cell")
        endpoints = ENDPOINT.findall(row["endpoint"])
        if not endpoints and "no endpoint" not in row["endpoint"].lower():
            problems.append(f"{where}: endpoint cell has neither an endpoint nor 'no endpoint'")
        for method, path in endpoints:
            if (method, path) not in openapi:
                problems.append(f"{where}: {method} {path} is not in v1.json")

    sections: dict[str, list[dict]] = {}
    for row in rows:
        sections.setdefault(row["section"], []).append(row)
    print(f"features in FEATURES-BY-SIDEBAR.md: {len(features)}")
    print(f"rows in the page map: {len(rows)}")
    for name, items in sections.items():
        none = sum(1 for item in items if "no endpoint" in item["endpoint"].lower() and not ENDPOINT.search(item["endpoint"]))
        print(f"  {name}: {len(items)} features, {none} with no endpoint")

    if problems:
        print(f"\nFAIL: {len(problems)} problem(s)")
        for problem in problems:
            print(f"  - {problem}")
        return 1
    print("\nOK: every feature is placed once and every endpoint exists in v1.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
