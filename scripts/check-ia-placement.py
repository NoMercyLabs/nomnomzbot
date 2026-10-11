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
# Keeps the app page registry (ShellPage.kt) and the UI placement ledger in step (slice S-IA-GUARD).
#   1. A ledger page or tab whose slice is no longer open in the plan must exist in ShellPage.kt.
#      A row whose slice is still open (its "- **S-...**" entry is in the plan) may be missing.
#   2. A page or tab in ShellPage.kt with no ledger row fails.
# Slug rule: the ledger name lower-cased, "&" dropped, every run of other non-letter-or-digit characters
# becomes one "-", no leading or trailing "-".
# Run: python scripts/check-ia-placement.py [--ledger P] [--plan P] [--pages P]

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_LEDGER = REPO / '.claude/docs/design/ui-placement-ledger-2026-10-11.md'
DEFAULT_PLAN = REPO / '.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md'
DEFAULT_PAGES = REPO / 'app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/core/navigation/ShellPage.kt'

# Rule 1: the viewer shell is a separate shell from ShellPage; its ledger rows start with this prefix.
VIEWER_SHELL_ROW_PREFIX = 'Participant:'
# Rule 2: tab cells that describe a mode or a layout, not a tab. Matched on the cell after
# parentheticals are dropped. Home lists role modes, Event Responses groups by event type, Scripts is the
# editor alone, Admin keeps its own 23 tabs inside the Admin chrome.
MODE_CELL_PREFIXES = ('Streamer:', 'Moderator:', 'Viewer:', '23 tabs unchanged')
MODE_CELLS = ('by event type', 'the editor itself')
# Rule 3: a dash cell means the page has no tabs.
NO_TABS_CELL = '-'

ENTRY = re.compile(r'^\s*\w+\("([^"]+)",\s*(?:emptyList\(\)|listOf\(([^)]*)\))')
SLICE_ID = re.compile(r'S-[A-Z0-9]+(?:-[A-Z0-9]+)*')
OPEN_SLICE = re.compile(r'^- \*\*(S-[A-Z0-9]+(?:-[A-Z0-9]+)*)\*\*', re.M)


def slug(name):
    text = name.lower().replace('&', '')
    return re.sub(r'[^a-z0-9]+', '-', text).strip('-')


def strip_parentheses(cell):
    return re.sub(r'\s*\([^)]*\)', '', cell).strip()


def is_mode_cell(cell):
    return cell.startswith(MODE_CELL_PREFIXES) or cell in MODE_CELLS


def split_tabs(cell):
    """Returns the tab names of a ledger cell; mode cells and the dash cell give none."""
    text = strip_parentheses(cell)
    if not text or text.startswith(NO_TABS_CELL) or is_mode_cell(text):
        return []
    return [part.strip() for part in text.split(';') if part.strip() and not is_mode_cell(part.strip())]


def parse_ledger(text):
    """Returns rows of (today, page, tab cell, slices) from the Pages table."""
    rows = []
    in_pages = False
    for line in text.splitlines():
        if line.startswith('## '):
            in_pages = line.strip() == '## Pages'
            continue
        if not in_pages or not line.startswith('|'):
            continue
        cells = [cell.strip() for cell in line.strip().strip('|').split('|')]
        if cells[0] == 'Today' or set(cells[0]) <= {'-'}:
            continue
        if len(cells) != 8:
            raise ValueError('ledger row has %d cells, expected 8: %s' % (len(cells), line))
        rows.append((cells[0], cells[3], cells[4], SLICE_ID.findall(cells[7])))
    return rows


def parse_pages(text):
    """Returns {page slug: set of tab slugs} from the ShellPage.kt enum entries."""
    pages = {}
    for line in text.splitlines():
        match = ENTRY.match(line)
        if match:
            pages[match.group(1)] = set(re.findall(r'"([^"]*)"', match.group(2) or ''))
    return pages


def parse_open_slices(text):
    return set(OPEN_SLICE.findall(text))


def check(ledger_text, plan_text, pages_text):
    open_slices = parse_open_slices(plan_text)
    rows = parse_ledger(ledger_text)
    pages = parse_pages(pages_text)
    problems = []
    placed_pages = set()
    placed_tabs = set()
    for today, page, tab_cell, slices in rows:
        page_slug = slug(page)
        placed_pages.add(page_slug)
        if today.startswith(VIEWER_SHELL_ROW_PREFIX):
            continue
        for tab in split_tabs(tab_cell):
            placed_tabs.add((page_slug, slug(tab)))
        closed = not any(item in open_slices for item in slices)
        if not closed:
            continue
        if page_slug not in pages:
            problems.append("ledger row '%s' (page '%s', slice %s) has no page '%s' in ShellPage.kt"
                            % (today, page, ', '.join(slices) or '-', page_slug))
            continue
        for tab in split_tabs(tab_cell):
            if slug(tab) not in pages[page_slug]:
                problems.append("ledger row '%s' (tab '%s' of '%s', slice %s) has no tab '%s/%s' in ShellPage.kt"
                                % (today, tab, page, ', '.join(slices) or '-', page_slug, slug(tab)))
    for page_slug, tabs in sorted(pages.items()):
        if page_slug not in placed_pages:
            problems.append("ShellPage.kt page '%s' has no row in the placement ledger" % page_slug)
            continue
        for tab_slug in sorted(tabs):
            if (page_slug, tab_slug) not in placed_tabs:
                problems.append("ShellPage.kt tab '%s/%s' has no row in the placement ledger" % (page_slug, tab_slug))
    return problems, len(pages), sum(len(tabs) for tabs in pages.values())


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument('--ledger', default=str(DEFAULT_LEDGER))
    parser.add_argument('--plan', default=str(DEFAULT_PLAN))
    parser.add_argument('--pages', default=str(DEFAULT_PAGES))
    args = parser.parse_args(argv)
    pages_path = pathlib.Path(args.pages)
    if not pages_path.exists():
        print('check-ia-placement: %s not found' % args.pages)
        return 1
    read = lambda path: pathlib.Path(path).read_text(encoding='utf-8')
    problems, page_count, tab_count = check(read(args.ledger), read(args.plan), read(pages_path))
    for problem in problems:
        print(problem)
    if problems:
        print('check-ia-placement: %d problem(s)' % len(problems))
        return 1
    print('check-ia-placement: %d page(s), %d tab(s) placed, 0 problem(s)' % (page_count, tab_count))
    return 0


if __name__ == '__main__':
    sys.exit(main())
