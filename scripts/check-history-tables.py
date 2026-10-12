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
# Every table that holds history gets a projection (owner 2026-10-12: "make sure you always remember this
# when we create a new storage table next time"). A live IEventHandler may write the row at once, but only an
# IProjection is refolded from the event journal, so an import (journal + projection rebuild) reaches only
# projected tables. ChatMessages had no projection and lost 13 months of imported chat.
# Fails when an IEventHandler file writes a DbSet no IProjection writes and EXEMPT does not list.
# Limit: it reads the handler file itself; a write made through an injected service is not seen.
# Run: python scripts/check-history-tables.py   (the pre-commit hook runs it when server/src .cs is staged)

import pathlib
import re
import subprocess
import sys

# Tables a live handler writes that are not history. Each needs a reason; the list only shrinks.
EXEMPT = {
    'Configurations': 'channel settings seeded at onboarding, not history',
    'EarningRules': 'earning rules seeded at onboarding, not history',
    'Rewards': 'current state of the Twitch reward definitions, not history',
    'Streams': 'DEBT 2026-10-12: history with no projection; S-STREAMS-PROJECTION adds it',
}

DB = r'\b_?(?:db|dbContext|context|ctx)\.([A-Z]\w*)'
WRITES = [
    re.compile(DB + r'\.(?:Add|AddAsync|AddRange|AddRangeAsync|Update|UpdateRange|Remove|RemoveRange|Attach)\('),
    re.compile(DB + r'\.[^;]*?\.Execute(?:Update|Delete)Async\('),
]
HANDLER = re.compile(r'\bIEventHandler<')
PROJECTION = re.compile(r':\s*[^{;]*\bIProjection\b')


def code_only(text):
    lines = []
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(('//', '*', '/*')):
            continue
        lines.append(line.split(' //', 1)[0])
    return chr(10).join(lines)


def written_tables(text):
    code = code_only(text)
    return {match.group(1) for pattern in WRITES for match in pattern.finditer(code)}


def writers(files, kind):
    tables = set()
    for text in files.values():
        if kind.search(code_only(text)):
            tables |= written_tables(text)
    return tables


def find_unprojected(files, exempt):
    handled = writers(files, HANDLER)
    projected = writers(files, PROJECTION)
    return sorted(handled - projected - set(exempt))


def find_stale(files, exempt):
    handled = writers(files, HANDLER)
    projected = writers(files, PROJECTION)
    return sorted(table for table in exempt if table not in handled or table in projected)


def tree_sources():
    root = pathlib.Path(subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True,
                                       text=True, check=True).stdout.strip())
    names = subprocess.run(['git', '-C', str(root), 'ls-files', 'server/src/*.cs'], capture_output=True,
                           text=True, encoding='utf-8', check=True).stdout.split()
    files = {}
    for name in names:
        path = root / name
        if '/Migrations' not in name and path.exists():
            files[name] = path.read_text(encoding='utf-8', errors='replace')
    return files


def main():
    files = tree_sources()
    unprojected = find_unprojected(files, EXEMPT)
    stale = find_stale(files, EXEMPT)
    for table in unprojected:
        print(f'{table}: a live IEventHandler writes it and no IProjection does. Add a projection so an import '
              f'and a rebuild reach it, or add it to EXEMPT in scripts/check-history-tables.py with the reason '
              f'it is not history.')
    for table in stale:
        print(f'{table}: listed in EXEMPT but no longer needs it (no handler writes it, or a projection does). '
              f'Remove the entry.')
    print(f'check-history-tables: {len(unprojected) + len(stale)} problem(s)')
    return 1 if unprojected or stale else 0


if __name__ == '__main__':
    sys.exit(main())
