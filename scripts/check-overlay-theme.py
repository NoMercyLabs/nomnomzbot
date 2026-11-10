#!/usr/bin/env python3
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------
"""Keeps every default overlay widget on the shared theme (S-OVL-GUARD).

A default widget draws from /overlay/theme.css: the --nnz-* tokens and the .nnz-* parts. Inside its <style> block
it may not name a font family, a literal colour, or a box shadow of its own, and its root must carry .nnz-widget.
A line that truly needs a literal (a game's own art, a player's black letterbox) ends with a comment that holds
`theme-exempt:` and the reason.

Usage: python scripts/check-overlay-theme.py [file.vue ...]   (no arguments = every default widget)
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets'

# Sound-only widgets draw nothing a viewer sees.
NOT_VISUAL = {'audio.vue', 'music_player.vue'}

STYLE_BLOCK = re.compile(r'<style[^>]*>(.*?)</style>', re.S)
COLOUR = re.compile(r'#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(|\boklch\(')
FONT = re.compile(r'font-family\s*:(?!\s*(var\(--nnz-font\)|inherit))')
SHADOW = re.compile(r'box-shadow\s*:(?!\s*(var\(--nnz-|none|inherit))')
EXEMPT = 'theme-exempt:'


def problems_in(path):
    text = path.read_text(encoding='utf-8')
    found = []
    if 'nnz-widget' not in text:
        found.append(f'{path.name}: the root element does not carry the nnz-widget class')
    offset = 0
    for block in STYLE_BLOCK.finditer(text):
        first_line = text.count('\n', 0, block.start(1)) + 1
        for index, line in enumerate(block.group(1).splitlines()):
            if EXEMPT in line:
                continue
            number = first_line + index
            if FONT.search(line):
                found.append(f'{path.name}:{number}: sets its own font family')
            if COLOUR.search(line):
                found.append(f'{path.name}:{number}: holds a literal colour')
            if SHADOW.search(line):
                found.append(f'{path.name}:{number}: sets its own box shadow')
        offset = block.end()
    return found


def main():
    named = [pathlib.Path(arg) for arg in sys.argv[1:]]
    files = named or sorted(p for p in ASSETS.glob('*.vue') if p.name not in NOT_VISUAL)
    problems = []
    for path in files:
        problems.extend(problems_in(path))
    for problem in problems:
        print(problem)
    print(f'check-overlay-theme: {len(problems)} problem(s) in {len(files)} file(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
