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
# Gate for rule docs-english-only (Stoney 2026-10-13, translated from Dutch: "I want you to translate all my
# words into English. I want no Dutch words of mine anywhere in documentation. Everyone must be able to
# understand this, and not everyone speaks Dutch."). Owner quotes are kept as faithful English translations.
# Fails on a line that holds three Dutch function words in a row. The Dutch translation files are skipped.
#
# Usage: python scripts/check-english-docs.py [path ...]

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_PATHS = [
    ROOT / 'CLAUDE.md',
    ROOT / '.claude' / 'docs',
    ROOT / '.claude' / 'skills',
    ROOT / '.scratch' / 'layout-mockups' / 'inventory',
    ROOT / 'dashboard' / 'src',
    ROOT / 'scripts',
]
SUFFIXES = {'.md', '.ts', '.vue', '.css', '.py', '.ps1', '.sh', '.cs', '.kt'}
WORD = (r'(?:niet|gewoon|moet|moeten|zodat|wordt|ervoor|eigenlijk|alsjeblieft|waarom|helemaal|natuurlijk|omdat'
        r'|hebben|kunnen|zou|doen|alles|ook|maar|nou|dus|graag|wil|mijn|zijn|heb|dit|deze|daar|nog|wel|even'
        r'|toch|bent|je|jij|wij|we|het|een|van|met|voor|naar|ik|als|dat|die|wat|er|op|te|en|bij|uit|ze|hij'
        r'|kan|geen|komt|gaan|gaat|staat|lekker|zie)')
DUTCH_RUN = re.compile(rf'\b{WORD}[^\w]+{WORD}[^\w]+{WORD}\b', re.IGNORECASE)
MESSAGE = 'Dutch text in a doc: write the owner quote as a faithful English translation'


def is_dutch_translation_file(path):
    parts = {part.lower() for part in path.parts}
    return path.stem.lower() == 'nl' or 'values-nl' in parts or path.name == 'check-english-docs.py'


def files_under(path):
    if path.is_file():
        return [path]
    return [file for file in path.rglob('*') if file.is_file() and file.suffix in SUFFIXES]


def check_file(path):
    problems = []
    text = path.read_bytes().decode('utf-8', errors='replace')
    for number, line in enumerate(text.splitlines(), start=1):
        if DUTCH_RUN.search(line):
            problems.append(f'{path.relative_to(ROOT) if path.is_relative_to(ROOT) else path}:{number}: {MESSAGE}')
    return problems


def main(arguments):
    roots = [pathlib.Path(argument).resolve() for argument in arguments] or DEFAULT_PATHS
    skip = ('node_modules', 'bin', 'obj', '__pycache__', 'dist')
    problems = []
    for root in roots:
        if not root.exists():
            continue
        for file in files_under(root):
            if any(part in skip for part in file.parts) or is_dutch_translation_file(file):
                continue
            problems.extend(check_file(file))
    for problem in problems:
        print(problem)
    print(f'check-english-docs: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
