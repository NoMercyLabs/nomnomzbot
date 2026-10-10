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
# Tests for scripts/check-owner-words.py.  Run: python scripts/check-owner-words_test.py

import importlib.util
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    'check_owner_words', pathlib.Path(__file__).with_name('check-owner-words.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def run(text):
    with tempfile.TemporaryDirectory() as tmp:
        path = pathlib.Path(tmp) / 'SHORTCOMINGS-EXECUTION-PLAN.md'
        path.write_text(text, encoding='utf-8')
        return gate.check_file(path)


class CheckOwnerWordsTests(unittest.TestCase):
    def test_a_quote_with_a_marked_proposal_passes(self):
        text = 'Owner, verbatim: "ban them all". Proposal, not settled: ban in channels that are on.\n'
        self.assertEqual([], run(text))

    def test_a_so_derivation_after_a_quote_fails(self):
        text = '- **S-X** Owner, verbatim: "entrust a mod".\n  So: (j) auto-accept is dropped.\n'
        problems = run(text)
        self.assertEqual(1, len(problems), problems)
        self.assertIn('SHORTCOMINGS-EXECUTION-PLAN.md:2', problems[0])
        self.assertIn('Proposal, not settled:', problems[0])

    def test_an_inline_so_derivation_fails(self):
        problems = run('Owner: "streamers can entrust". So: the reach needs an entrust.\n')
        self.assertEqual(1, len(problems), problems)

    def test_an_arc_reading_label_passes(self):
        self.assertEqual([], run("Arc's reading (withdrawn below): (f) the owner skips the step.\n"))

    def test_so_inside_a_word_or_lower_case_passes(self):
        self.assertEqual([], run('Also: the queue. So the bot waits, and also: it logs.\n'))


if __name__ == '__main__':
    unittest.main()
