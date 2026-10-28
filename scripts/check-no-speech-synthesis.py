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
#
# Browser speech is gone (Stoney 2026-10-05: "edge tts is available everywhere and must be the standard.
# that lame outdated browser native tts goes away"; 2026-10-19: "i told you to completely remove it 1 month
# ago"). Bot speech is server-made audio only. This gate fails when shipped source mentions the browser
# speech API: speechSynthesis or SpeechSynthesisUtterance.
#
# Scope: tracked files under server/src, dashboard/src and app/ (.cs .ts .vue .kt .js .html).
# Usage: python scripts/check-no-speech-synthesis.py        (self-test: scripts/check-no-speech-synthesis_test.py)

import pathlib
import re
import subprocess
import sys

PATTERN = re.compile(r'speechSynthesis|SpeechSynthesisUtterance', re.IGNORECASE)
ROOTS = ('server/src/', 'dashboard/src/', 'app/')
EXTENSIONS = ('.cs', '.ts', '.vue', '.kt', '.js', '.html')
SKIPPED_PARTS = ('node_modules/', '/bin/', '/obj/', '/build/')


def in_scope(path):
    normalized = path.replace('\\', '/')
    return (normalized.startswith(ROOTS)
            and normalized.endswith(EXTENSIONS)
            and not any(part in '/' + normalized for part in SKIPPED_PARTS))


def find_hits(files):
    """files: {path: text}. Returns [(path, line number, line)] for every in-scope mention."""
    hits = []
    for path, text in files.items():
        if not in_scope(path):
            continue
        for number, line in enumerate(text.splitlines(), start=1):
            if PATTERN.search(line):
                hits.append((path, number, line.strip()))
    return hits


def tracked_files(root):
    listing = subprocess.run(['git', '-C', str(root), 'ls-files', '--cached', '--others',
                              '--exclude-standard'], capture_output=True, text=True, check=True)
    return [p for p in listing.stdout.splitlines() if in_scope(p)]


def main():
    root = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parent.parent
    files = {}
    for path in tracked_files(root):
        full = root / path
        if full.is_file():
            files[path] = full.read_text(encoding='utf-8', errors='ignore')
    hits = find_hits(files)
    for path, number, line in hits:
        print(f'{path}:{number}: {line}')
    if hits:
        print(f'check-no-speech-synthesis: {len(hits)} browser speech mention(s). '
              'Bot speech is server audio only (Stoney 2026-10-05, 2026-10-19).')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
