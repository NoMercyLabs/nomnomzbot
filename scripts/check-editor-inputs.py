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
# Gate for rule ux-ask-once (Stoney 2026-10-07: "a json action editor in a manual block instead of utilizing
# the full features of a vscode style editor"). Code, JSON or data typed inside the widget editor is a Monaco
# model, never a <textarea> or contenteditable. Fails on any such input in the editor assets, except the ids in
# ALLOWED (plain key=value fields that are not code).
#
# Usage: python scripts/check-editor-inputs.py [--dir <editor assets dir>]

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
EDITOR = ROOT / 'server' / 'src' / 'NomNomzBot.Api' / 'Assets' / 'editor'
ALLOWED = {'testRunVars'}
INPUT = re.compile(r"<textarea\b[^>]*>|createElement\(\s*['\"]textarea['\"]\s*\)|contenteditable", re.I)
ELEMENT_ID = re.compile(r'\bid="([^"]+)"')


def problems_in(path):
    problems = []
    for number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), start=1):
        for match in INPUT.finditer(line):
            element_id = ELEMENT_ID.search(match.group(0))
            if element_id and element_id.group(1) in ALLOWED:
                continue
            problems.append(f'{path.name}:{number}: a text input for code or data; use a Monaco model: {match.group(0)[:80]}')
    return problems


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dir', default=str(EDITOR))
    folder = pathlib.Path(parser.parse_args().dir)
    problems = []
    for path in sorted(folder.glob('*.html')) + sorted(folder.glob('*.js')):
        problems.extend(problems_in(path))
    for problem in problems:
        print(problem)
    print(f'check-editor-inputs: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
