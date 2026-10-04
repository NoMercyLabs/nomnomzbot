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
# Tests for scripts/check-parity-ledger.py.  Run: python scripts/check-parity-ledger_test.py

import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    'check_parity_ledger', pathlib.Path(__file__).with_name('check-parity-ledger.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

HEADER = '| Legacy item | Legacy file:line | Status | NomNomzBot | Gap | Stream-facing | Slice |\n|---|---|---|---|---|---|---|\n'


def ledger(commands_rows, tts_rows=''):
    return '# Legacy parity ledger\n\n## Commands\n\n' + HEADER + commands_rows + '\n## TTS\n\n' + HEADER + tts_rows


INVENTORY = [('Commands', 'Lurk.cs', None), ('Commands', 'Hug.cs', None), ('TTS', 'Lurk.cs', '108')]
PLAN = '- **S-PAR-CMD-HUG** hug parity\n- **S-PAR-TTS-BROADCASTER-VOICE** voice\n'
GOOD = ledger(
    '| command `Lurk` | commands/Lurk.cs:15 | matched | x | | yes | |\n'
    '| command `Hug` | commands/Hug.cs:13 | partial | x | one pool | yes | S-PAR-CMD-HUG |\n',
    '| tts call | commands/Lurk.cs:108 | partial | x | viewer voice | yes | S-PAR-TTS-BROADCASTER-VOICE |\n')


class CheckParityLedgerTests(unittest.TestCase):
    def test_a_complete_ledger_with_planned_slices_passes(self):
        self.assertEqual([], gate.check(GOOD, PLAN, INVENTORY))

    def test_a_legacy_item_missing_from_the_ledger_fails_and_is_named(self):
        errors = gate.check(GOOD, PLAN, INVENTORY + [('Commands', 'Karen.cs', None)])
        self.assertEqual(1, len(errors))
        self.assertIn('Karen.cs', errors[0])

    def test_a_tts_call_site_is_matched_by_file_and_line(self):
        errors = gate.check(GOOD, PLAN, INVENTORY + [('TTS', 'Lurk.cs', '115')])
        self.assertEqual(1, len(errors))
        self.assertIn('Lurk.cs:115', errors[0])

    def test_a_gap_row_without_a_slice_fails(self):
        text = GOOD.replace('| one pool | yes | S-PAR-CMD-HUG |', '| one pool | yes | |')
        errors = gate.check(text, PLAN, INVENTORY)
        self.assertEqual(1, len(errors))
        self.assertIn('Hug', errors[0])

    def test_a_gap_whose_slice_is_not_in_the_plan_fails(self):
        errors = gate.check(GOOD, '- **S-PAR-TTS-BROADCASTER-VOICE** voice\n', INVENTORY)
        self.assertEqual(1, len(errors))
        self.assertIn('S-PAR-CMD-HUG', errors[0])

    def test_a_row_with_an_unknown_status_fails(self):
        text = GOOD.replace('| matched |', '| not checked |')
        errors = gate.check(text, PLAN, INVENTORY)
        self.assertEqual(1, len(errors))
        self.assertIn('Lurk', errors[0])

    def test_a_matched_row_needs_no_slice_and_no_plan_entry(self):
        text = GOOD.replace('| partial | x | one pool | yes | S-PAR-CMD-HUG |', '| matched | x | | yes | |')
        self.assertEqual([], gate.check(text, '- **S-PAR-TTS-BROADCASTER-VOICE** voice\n', INVENTORY))


if __name__ == '__main__':
    unittest.main()
