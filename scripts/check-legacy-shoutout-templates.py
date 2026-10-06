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
# Data-parity gate for rule legacy-parity-ledger (Stoney 2026-10-06: "make sure you have all the custom
# shoutout templates properly copied over to the new bot"). check-parity-ledger.py covers old-bot code;
# this covers old-bot channel data. Fails when a legacy per-channel shoutout template is missing from the
# live bot, differs from it, uses a placeholder the announcement never fills, or when the live channel
# default is set. The old bot's default template meant "pick a random snarky line"; the new bot does the
# same only while the channel template is empty (the personality pool), so a non-empty default is a defect.
#
# Live export (one broadcaster), run against the live Postgres with psql:
#   \copy (select "TargetTwitchUserId", "MessageTemplate" from "ShoutoutOverrides"
#          where "BroadcasterId" = '<id>' and "Kind" = 'shoutout' and "DeletedAt" is null) to stdout with csv header
#   \copy (select "ShoutoutTemplate" from "Channels" where "Id" = '<id>') to stdout with csv
#
# Usage: python scripts/check-legacy-shoutout-templates.py --live-csv <export> [--legacy-db <sqlite>]

import argparse
import collections
import csv
import io
import pathlib
import re
import sqlite3
import sys

DEFAULT_LEGACY_DB = pathlib.Path.home() / 'AppData/Local/NoMercyBot/data/database.sqlite'

# Live spelling of a legacy placeholder: the announcement seeds {target.game} / {target.link}, not the bare names.
RENAMES = {'game': 'target.game', 'link': 'target.link'}
# Placeholders the shoutout announcement fills (ShoutoutAction.ComposeAnnouncementAsync + TemplateResolver).
RESOLVED = {'name', 'subject', 'object', 'possessive', 'tense', 'presenttense', 'genderedterm',
            'target', 'target.id', 'target.name', 'target.link', 'target.game'}
PLACEHOLDER = re.compile('[{]([^{}]+)[}]')


def live_spelling(template):
    return PLACEHOLDER.sub(lambda m: '{' + RENAMES.get(m.group(1).lower(), m.group(1)) + '}', template)


def unresolved(template):
    keys = (m.group(1).lower() for m in PLACEHOLDER.finditer(template))
    return sorted({k for k in keys if k not in RESOLVED and not k.startswith('verb:')})


def legacy_templates(db_path):
    """(default template, {twitch id: (name, custom template)}) from the old bot, opened read-only."""
    conn = sqlite3.connect(f'file:{db_path.as_posix()}?mode=ro', uri=True)
    rows = [(str(i), n, (t or '').strip()) for i, n, t in conn.execute('select Id, Name, ShoutoutTemplate from Channels')]
    default = collections.Counter(t for _, _, t in rows if t).most_common(1)[0][0]
    return default, {i: (n, t) for i, n, t in rows if t and t != default}


def live_templates(csv_path):
    """({twitch id: template}, channel default) from the two-part live export."""
    header, rest = csv_path.read_text(encoding='utf-8').split('\n', 1)
    # Drop only the final newline: an empty (NULL) default is an empty last line, not a missing one.
    body, _, default_line = rest.removesuffix('\n').rpartition('\n')
    reader = csv.DictReader(io.StringIO(header + '\n' + body))
    overrides = {r['TargetTwitchUserId']: r['MessageTemplate'].strip() for r in reader}
    default = (next(csv.reader([default_line]), None) or [''])[0].strip()
    return overrides, default


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--live-csv', type=pathlib.Path, required=True)
    parser.add_argument('--legacy-db', type=pathlib.Path, default=DEFAULT_LEGACY_DB)
    args = parser.parse_args()

    _, legacy_custom = legacy_templates(args.legacy_db)
    live, live_default = live_templates(args.live_csv)

    problems = []
    if live_default:
        problems.append(f'channel default is set ({live_default!r}); it must be empty so the snarky pool is used')
    for twitch_id, (name, template) in sorted(legacy_custom.items(), key=lambda kv: kv[1][0].lower()):
        if twitch_id not in live:
            problems.append(f'missing: {name} ({twitch_id})')
        elif live[twitch_id] != live_spelling(template):
            problems.append(f'differs: {name} ({twitch_id})')
        elif unresolved(live[twitch_id]):
            problems.append(f'unfilled placeholders {unresolved(live[twitch_id])}: {name} ({twitch_id})')

    print(f'legacy custom {len(legacy_custom)}, live overrides {len(live)}, problems {len(problems)}')
    for line in problems:
        print('  ' + line)
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
