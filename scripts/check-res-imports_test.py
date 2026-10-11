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
# Tests for scripts/check-res-imports.py.  Run: python scripts/check-res-imports_test.py

import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    'check_res_imports', pathlib.Path(__file__).with_name('check-res-imports.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

PREFIX = 'import nomnomzbot.composeapp.generated.resources.'


def kotlin(*lines):
    return chr(10).join(['package bot.nomnomz.dashboard.feature.x'] + list(lines))


class ResImportGate(unittest.TestCase):
    def test_a_used_string_without_its_import_fails(self):
        # 2026-10-09, 51b0fd13d: CommandsScreen.kt used commands_dialog_speak_with_tts with no import.
        text = kotlin(PREFIX + 'commands_dialog_save',
                      'val a = stringResource(Res.string.commands_dialog_save)',
                      'val b = stringResource(Res.string.commands_dialog_speak_with_tts)')
        self.assertEqual(['commands_dialog_speak_with_tts'], gate.find_missing(text))

    def test_every_resource_kind_is_checked(self):
        text = kotlin('val p = pluralStringResource(Res.plurals.items_count, 2)',
                      'val i = painterResource(Res.drawable.logo)')
        self.assertEqual(['items_count', 'logo'], gate.find_missing(text))

    def test_imported_uses_pass(self):
        text = kotlin(PREFIX + 'commands_dialog_save', 'stringResource(Res.string.commands_dialog_save)')
        self.assertEqual([], gate.find_missing(text))

    def test_a_wildcard_import_covers_every_use(self):
        text = kotlin(PREFIX + '*', 'stringResource(Res.string.anything_at_all)')
        self.assertEqual([], gate.find_missing(text))

    def test_a_use_inside_a_comment_is_not_a_use(self):
        text = kotlin('// the screen wraps it in Res.string.setup_error_bot',
                      ' * resolves it through `Res.string.obs_no_channel_error`',
                      'val x = 1 // see Res.string.trailing_comment')
        self.assertEqual([], gate.find_missing(text))


if __name__ == '__main__':
    unittest.main()
