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
# Gate for rule owner-words-not-inferred (Stoney 2026-10-10, on the shared-doc line "We will not turn on a
# cross-channel feature for you": "this means that i do not get the one click ban from all channels i mod for
# option that i desire as a mod of 32 channels"). A derivation written as "So: ..." after an owner quote read
# as settled text, and it overrode what the owner asked for. Derived text must carry its status:
# "Arc's reading:" or "Proposal, not settled:". Fails on every "So: " that starts a derivation.
#
# Usage: python scripts/check-owner-words.py [--dir <design dir>]

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_DIR = ROOT / '.claude' / 'docs' / 'design'
DERIVATION = re.compile(r'(?<![A-Za-z0-9_])So: ')
MESSAGE = 'derived text must start "Arc\'s reading:" or "Proposal, not settled:", not "So:"'


def check_file(path):
    path = pathlib.Path(path)
    problems = []
    lines = path.read_bytes().decode('utf-8').splitlines()
    for number, line in enumerate(lines, start=1):
        if DERIVATION.search(line):
            problems.append(f'{path.name}:{number}: {MESSAGE}')
    return problems


def targets(directory):
    directory = pathlib.Path(directory)
    plan = directory / 'SHORTCOMINGS-EXECUTION-PLAN.md'
    found = [plan] if plan.exists() else []
    return found + sorted(directory.glob('*research*.md'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dir', default=str(DEFAULT_DIR))
    args = parser.parse_args()
    problems = []
    for path in targets(args.dir):
        problems += check_file(path)
    for problem in problems:
        print(problem)
    if problems:
        print(f'FAIL: {len(problems)} inferred line(s) read as settled owner words')
        return 1
    print('ok: no inferred line reads as settled owner words')
    return 0


if __name__ == '__main__':
    sys.exit(main())
