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
# Every Compose resource a Kotlin file uses (Res.string.x, Res.plurals.x, ...) needs its own import, or the
# app does not compile. jvmTest can pass while compileKotlinWasmJs fails (2026-10-09, 51b0fd13d left the web
# build broken). Reads the STAGED Kotlin files and fails on a use without its import.
# Run: python scripts/check-res-imports.py           (the pre-commit hook runs it on staged .kt files)
#      python scripts/check-res-imports.py --all     (the whole app tree)

import pathlib
import re
import subprocess
import sys

USE = re.compile(r'Res\.(?:string|plurals|drawable|font|array|files)\.([A-Za-z0-9_]+)')
IMPORT = re.compile(r'^import nomnomzbot\.composeapp\.generated\.resources\.([A-Za-z0-9_*]+)', re.M)


def code_only(text):
    lines = []
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(('//', '*', '/*')):
            continue
        lines.append(line.split(' //', 1)[0])
    return chr(10).join(lines)


def find_missing(text):
    imported = set(IMPORT.findall(text))
    if '*' in imported:
        return []
    used = sorted(set(USE.findall(code_only(text))))
    return [name for name in used if name not in imported]


def staged_kotlin():
    names = subprocess.run(['git', 'diff', '--cached', '--name-only', '--diff-filter=ACMR', '--', '*.kt'],
                           capture_output=True, text=True, encoding='utf-8', check=True).stdout.split()
    for name in names:
        text = subprocess.run(['git', 'show', ':' + name], capture_output=True, text=True,
                              encoding='utf-8', check=True).stdout
        yield name, text


def tree_kotlin():
    root = pathlib.Path(subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True,
                                       text=True, check=True).stdout.strip()) / 'app'
    for path in root.rglob('*.kt'):
        if 'build' not in path.parts:
            yield str(path), path.read_text(encoding='utf-8', errors='replace')


def main():
    files = tree_kotlin() if '--all' in sys.argv else staged_kotlin()
    problems = [(name, missing) for name, text in files for missing in find_missing(text)]
    for name, missing in problems:
        print(f'{name}: Res use {missing} has no import nomnomzbot.composeapp.generated.resources.{missing}')
    print(f'check-res-imports: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
