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
# Gate for rule legacy-parity-ledger (Stoney 2026-10-04: "i am frustrated that i have to ask once more to
# match behavior from my old bot"). Fails when:
#   - a legacy command, reward, event handler, background service or TTS call site (listed by script from
#     the legacy repo) has no row in .claude/docs/design/LEGACY-PARITY-LEDGER.md;
#   - a ledger row has a status other than matched / partial / missing / n/a;
#   - a partial or missing row names no slice, or names a slice that is not in SHORTCOMINGS-EXECUTION-PLAN.md.
# Closing a parity slice therefore means: fix it, set its ledger rows to matched, then delete the slice.
#
# Usage: python scripts/check-parity-ledger.py [--legacy <nomercy-bot/src>] [--ledger <md>] [--plan <md>]

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
STATUSES = ('matched', 'partial', 'missing', 'n/a')
CELL_SPLIT = re.compile(r'(?<!\\)\|')
LEGACY_FILE = re.compile(r'([A-Za-z0-9_.-]+\.cs)(?::(\d+))?')
TTS_CALL = re.compile(r'SendCachedTts|SendTts\w*\(|[Tt]tsService\.\w+\(|SpeakAsync\(')
TWITCH_SUB = re.compile(r'\.(\w+)\s*\+=\s*(On\w+)')
EVENT_SUB = re.compile(r'\.(\w+)\s*\+=\s*(On\w+|async)')
HOSTED = re.compile(r':\s*(BackgroundService|IHostedService)|PeriodicTimer|new Timer\(|System\.Timers')


def cs_files(root):
    return sorted(p for p in root.rglob('*.cs') if not {'obj', 'bin'} & set(p.parts))


def legacy_inventory(src):
    """(area, file name, line or None) for every legacy item the ledger must cover."""
    commands_rewards = src / 'NoMercyBot.CommandsRewards'
    services = src / 'NoMercyBot.Services'
    items = [('Commands', p.name, None) for p in cs_files(commands_rewards / 'commands')]
    items += [('Rewards', p.name, None) for d in ('rewards', 'changes', 'widgets') for p in cs_files(commands_rewards / d)]
    for p in cs_files(services / 'Twitch' / 'EventHandlers'):
        items += [('Events', p.name, None) for _ in TWITCH_SUB.finditer(p.read_text(encoding='utf-8', errors='replace'))]
    for d in ('Obs', 'Discord', 'Spotify'):
        for p in cs_files(services / d):
            items += [('Events', p.name, None) for _ in EVENT_SUB.finditer(p.read_text(encoding='utf-8', errors='replace'))]
    for p in cs_files(services):
        if HOSTED.search(p.read_text(encoding='utf-8', errors='replace')):
            items.append(('Services and timers', p.name, None))
    for p in cs_files(commands_rewards) + cs_files(services):
        if 'TTS' in p.parts or p.stem == 'TTSService':
            continue
        for number, line in enumerate(p.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
            if TTS_CALL.search(line):
                items.append(('TTS', p.name, str(number)))
    return items


def ledger_rows(text):
    rows = []
    area = None
    for line in text.splitlines():
        heading = re.match(r'^##\s+(.+?)\s*$', line)
        if heading:
            area = heading.group(1)
            continue
        if not line.startswith('|') or re.match(r'^\|\s*(-|Legacy item)', line):
            continue
        cells = [c.strip() for c in CELL_SPLIT.split(line.strip())[1:-1]]
        cells += [''] * (7 - len(cells))
        files = LEGACY_FILE.findall(cells[1].replace('`', ''))
        rows.append({'area': area, 'item': re.sub(r'[`*]', '', cells[0]), 'files': files,
                     'status': cells[2].lower(), 'slice': cells[6]})
    return rows


def covered(rows, area, name, number):
    for row in rows:
        if row['area'] != area:
            continue
        for file_name, line in row['files']:
            if file_name == name and (number is None or line == number):
                return True
    return False


def check(ledger_text, plan_text, inventory):
    rows = ledger_rows(ledger_text)
    errors = []
    for area, name, number in inventory:
        if not covered(rows, area, name, number):
            where = f'{name}:{number}' if number else name
            errors.append(f'{area}: legacy {where} has no ledger row')
    for row in rows:
        label = f"{row['area']}: {row['item']}"
        if row['status'] not in STATUSES:
            errors.append(f"{label}: status '{row['status']}' is not one of {', '.join(STATUSES)}")
            continue
        if row['status'] in ('partial', 'missing'):
            if not row['slice']:
                errors.append(f'{label}: a {row["status"]} row names no slice')
            elif f"**{row['slice']}**" not in plan_text:
                errors.append(f"{label}: slice {row['slice']} is not in the plan")
    return errors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--legacy', default='C:/Projects/StoneyEagle/nomercy-bot/src')
    parser.add_argument('--ledger', default=str(ROOT / '.claude/docs/design/LEGACY-PARITY-LEDGER.md'))
    parser.add_argument('--plan', default=str(ROOT / '.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md'))
    args = parser.parse_args()
    legacy = pathlib.Path(args.legacy)
    if not legacy.is_dir():
        print(f'legacy repo not found at {legacy}: coverage cannot be checked')
        return 2
    inventory = legacy_inventory(legacy)
    errors = check(pathlib.Path(args.ledger).read_text(encoding='utf-8'),
                   pathlib.Path(args.plan).read_text(encoding='utf-8'), inventory)
    for error in errors:
        print(error)
    print(f'{len(inventory)} legacy items checked; {len(errors)} problems')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
