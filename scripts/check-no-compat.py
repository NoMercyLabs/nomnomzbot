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
# V0 has no users to protect: no line may plan or add backwards-compatibility code (owner 2026-10-11:
# "and we are V0 so no backwards compattible bullshit tangled in the code"). Reads the STAGED diff and
# fails on an added line that does; a line that forbids compat ("no alias map") passes.
# Run: python scripts/check-no-compat.py   (the pre-commit hook runs it on every commit)

import re
import subprocess
import sys

PATTERNS = [
    re.compile(r'alias map', re.I),
    re.compile(r'\bold (address|route|endpoint|url|field|name)e?s?\b[^.]*\b(keeps?|still) (working|resolv)', re.I),
    re.compile(r'backwards?[- ]compat', re.I),
    re.compile(r'compat(ibility)? (shim|layer|alias)', re.I),
    re.compile(r'\bfor old clients\b', re.I),
    re.compile(r'legacy (alias|route|shim)', re.I),
    re.compile(r'\[Obsolete\b'),
    re.compile(r'@Deprecated\b'),
]
NEGATION = re.compile(r'\b(no|not|never|without)\b', re.I)
EXEMPT = ('scripts/check-no-compat.py', 'scripts/check-no-compat_test.py')


def is_hit(line):
    for pattern in PATTERNS:
        match = pattern.search(line)
        if match and not NEGATION.search(line[:match.start()]):
            return True
    return False


def find_hits(diff_text):
    hits = []
    path = None
    for line in diff_text.splitlines():
        if line.startswith('+++ '):
            path = line[6:] if line.startswith('+++ b/') else None
            continue
        if path is None or path in EXEMPT or not line.startswith('+'):
            continue
        added = line[1:]
        if is_hit(added):
            hits.append((path, added))
    return hits


def main():
    staged = subprocess.run(['git', 'diff', '--cached', '-U0', '--diff-filter=ACMR'],
                            capture_output=True, text=True, encoding='utf-8', check=True).stdout
    hits = find_hits(staged)
    for path, line in hits:
        print(f'{path}: {line.strip()}')
    if hits:
        print(f'check-no-compat: {len(hits)} added line(s) plan or add backwards-compat code. V0 breaks freely.')
        return 1
    print('check-no-compat: 0 problem(s)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
