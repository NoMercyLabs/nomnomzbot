# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# Gate for rule research-needs-coverage-ledger (Stoney 2026-10-10: "and you are sure you covered all
# bases?" after a research paper was called done with 31 "not checked" items and whole topics missing).
# A research paper is done only against a closed coverage ledger. Fails when a
# .claude/docs/design/*research*.md file:
#   - has no "Coverage ledger" section with columns Topic | Origin | Status | Section | Reason;
#   - lists fewer than 15 topics (a handful of rows is a table of contents, not a ledger);
#   - has a row whose status is not closed or blocked;
#   - has a closed row that names no section of the paper, or a blocked row with no reason;
#   - has a row with no origin (owner ask, code, benchmark, outside source);
#   - has no "Closed N of M" line, or one that does not match the rows;
#   - says "not checked" anywhere outside a blocked ledger row (an unchecked item is either checked
#     or carried in the ledger as blocked, with the reason).
#
# Usage: python scripts/check-research-coverage.py [--dir <design dir>]

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DESIGN = ROOT / '.claude' / 'docs' / 'design'
STATUSES = ('closed', 'blocked')
MIN_TOPICS = 15
HEADER_COLUMNS = ('topic', 'origin', 'status', 'section', 'reason')
CELL_SPLIT = re.compile(r'(?<!\\)\|')
LEDGER = re.compile(r'^(#{2,4}) [^\n]*Coverage ledger[^\n]*$', re.M | re.I)
CLOSED_LINE = re.compile(r'Closed (\d+) of (\d+)')
NOT_CHECKED = re.compile(r'not checked', re.I)


def ledger_section(text):
    match = LEDGER.search(text)
    if not match:
        return None, text
    level = len(match.group(1))
    rest = text[match.end():]
    end = re.search(r'^#{1,%d} ' % level, rest, flags=re.M)
    body = rest[:end.start()] if end else rest
    outside = text[:match.start()] + (rest[end.start():] if end else '')
    return body, outside


def table_rows(text):
    rows = []
    for line in text.splitlines():
        if not line.strip().startswith('|'):
            continue
        cells = [c.strip() for c in CELL_SPLIT.split(line.strip().strip('|'))]
        if all(re.fullmatch(r':?-{2,}:?', c) for c in cells if c):
            continue
        rows.append(cells)
    return rows


def check_paper(path):
    problems = []
    text = path.read_text(encoding='utf-8')
    name = path.name

    body, outside = ledger_section(text)
    if body is None:
        return [f'{name}: no "Coverage ledger" section']

    rows = table_rows(body)
    header = next((r for r in rows if [c.lower() for c in r[:5]] == list(HEADER_COLUMNS)), None)
    if header is None:
        return [f'{name}: no ledger table with columns {" | ".join(HEADER_COLUMNS)}']

    topics = [r for r in rows if r is not header and len(r) >= 5]
    if len(topics) < MIN_TOPICS:
        problems.append(f'{name}: {len(topics)} topics, need at least {MIN_TOPICS}')

    closed = 0
    for row in topics:
        topic, origin, status, section, reason = row[:5]
        label = f'{name}: "{topic}"'
        status = status.lower()
        if status not in STATUSES:
            problems.append(f'{label}: status "{status}" is not one of {", ".join(STATUSES)}')
            continue
        if not origin:
            problems.append(f'{label}: no origin for the topic')
        if status == 'closed':
            closed += 1
            if not section:
                problems.append(f'{label}: closed but names no section of the paper')
        if status == 'blocked' and len(reason) < 10:
            problems.append(f'{label}: blocked needs a reason')
        if status == 'closed' and NOT_CHECKED.search(' '.join(row)):
            problems.append(f'{label}: closed but says "not checked"')

    summary = CLOSED_LINE.search(body)
    if not summary:
        problems.append(f'{name}: no "Closed N of M" line in the ledger')
    elif (int(summary.group(1)), int(summary.group(2))) != (closed, len(topics)):
        problems.append(
            f'{name}: ledger says Closed {summary.group(1)} of {summary.group(2)}, '
            f'rows say Closed {closed} of {len(topics)}')

    for line in outside.splitlines():
        if NOT_CHECKED.search(line):
            problems.append(f'{name}: "not checked" outside the ledger: {line.strip()[:100]}')
    return problems


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dir', default=str(DESIGN))
    args = parser.parse_args()

    problems = []
    for path in sorted(pathlib.Path(args.dir).glob('*research*.md')):
        problems.extend(check_paper(path))

    for problem in problems:
        print(problem)
    print(f'check-research-coverage: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
