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
# Fails when a PowerShell script without a UTF-8 BOM holds a non-ASCII character. Windows PowerShell 5.1
# reads such a file as Windows-1252, where the last byte of a UTF-8 em dash is a curly double quote that
# PowerShell treats as a quote mark: one em dash in a code line breaks every string after it.

import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parent.parent
tracked = subprocess.run(
    ['git', '-C', str(root), 'ls-files', '--', '*.ps1'], capture_output=True, text=True, check=True
).stdout.split()
bad = []
for name in tracked:
    path = root / name
    if not path.exists():
        continue
    raw = path.read_bytes()
    if raw.startswith(b'\xef\xbb\xbf'):
        continue
    for number, line in enumerate(raw.decode('utf-8', errors='replace').split('\n'), start=1):
        if any(ord(c) > 127 for c in line):
            bad.append(f'{path.relative_to(root)}:{number}')

if bad:
    print(f'{len(bad)} non-ASCII line(s) in BOM-less PowerShell scripts (use plain ASCII):')
    print('\n'.join(bad[:40]))
    sys.exit(1)
print('PowerShell scripts are ASCII-safe.')
