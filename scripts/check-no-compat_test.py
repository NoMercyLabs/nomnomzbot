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
# Tests for scripts/check-no-compat.py.  Run: python scripts/check-no-compat_test.py

import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    'check_no_compat', pathlib.Path(__file__).with_name('check-no-compat.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def diff(path, *added):
    lines = ['diff --git a/' + path + ' b/' + path, '+++ b/' + path, '@@ -1,0 +1,9 @@']
    lines += ['+' + line for line in added]
    lines += [' unchanged context with an alias map', '-removed line with backwards compat']
    return chr(10).join(lines)


class NoCompatGate(unittest.TestCase):
    def test_alias_map_in_a_plan_fails(self):
        hits = gate.find_hits(diff('plan.md', 'and an alias map for every old address'))
        self.assertEqual([('plan.md', 'and an alias map for every old address')], hits)

    def test_old_addresses_that_keep_working_fail(self):
        hits = gate.find_hits(diff('paper.md', '- Every old address keeps working.'))
        self.assertEqual(1, len(hits))

    def test_obsolete_attribute_in_code_fails(self):
        hits = gate.find_hits(diff('Api/Foo.cs', '    [Obsolete("use Bar")]'))
        self.assertEqual([('Api/Foo.cs', '    [Obsolete("use Bar")]')], hits)

    def test_kotlin_deprecated_fails(self):
        self.assertEqual(1, len(gate.find_hits(diff('app/Foo.kt', '@Deprecated("old route")'))))

    def test_compat_shim_comment_fails(self):
        self.assertEqual(1, len(gate.find_hits(diff('Foo.cs', '// backwards-compatible shim for old clients'))))

    def test_a_line_that_forbids_compat_passes(self):
        self.assertEqual([], gate.find_hits(diff('paper.md', 'V0: no backwards compatibility, no alias map.')))

    def test_context_and_removed_lines_are_ignored(self):
        self.assertEqual([], gate.find_hits(diff('paper.md', 'a clean new line')))

    def test_browser_compatibility_build_passes(self):
        self.assertEqual([], gate.find_hits(diff('plan.md', 'an old browser gets the JS compatibility build')))

    def test_the_gate_script_and_its_test_are_exempt(self):
        self.assertEqual([], gate.find_hits(diff('scripts/check-no-compat.py', 'PATTERN = "alias map"')))


if __name__ == '__main__':
    unittest.main()
