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
# Tests for scripts/check-research-coverage.py.  Run: python scripts/check-research-coverage_test.py

import importlib.util
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    'check_research_coverage', pathlib.Path(__file__).with_name('check-research-coverage.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

HEADER = '| Topic | Origin | Status | Section | Reason |\n|---|---|---|---|---|\n'


def closed_rows(count):
    return ''.join(f'| topic {i} | owner ask | closed | 5.{i} | |\n' for i in range(count))


def paper(rows, summary=None, body='## 1. Findings\n\nAll checked.\n\n'):
    total = rows.count('\n')
    closed = rows.count('| closed |')
    line = summary if summary is not None else f'Closed {closed} of {total}.\n\n'
    return '# Research\n\n' + body + '## 11. Coverage ledger\n\n' + line + HEADER + rows


def run(text):
    with tempfile.TemporaryDirectory() as tmp:
        path = pathlib.Path(tmp) / 'topic-research-2026-10-10.md'
        path.write_text(text, encoding='utf-8')
        return gate.check_paper(path)


class CheckResearchCoverageTests(unittest.TestCase):
    def test_a_closed_ledger_passes(self):
        self.assertEqual([], run(paper(closed_rows(15))))

    def test_a_paper_without_a_ledger_fails(self):
        problems = run('# Research\n\n## 1. Findings\n\nDone.\n')
        self.assertEqual(['topic-research-2026-10-10.md: no "Coverage ledger" section'], problems)

    def test_a_few_topics_is_not_a_ledger(self):
        problems = run(paper(closed_rows(4)))
        self.assertTrue(any('4 topics, need at least 15' in p for p in problems), problems)

    def test_an_open_topic_fails(self):
        rows = closed_rows(15) + '| x live | owner ask | open | | |\n'
        problems = run(paper(rows))
        self.assertTrue(any('status "open"' in p for p in problems), problems)

    def test_blocked_needs_a_reason_and_closed_needs_a_section(self):
        rows = closed_rows(14) + '| kick api | code | blocked | | no |\n| tts | owner ask | closed | | |\n'
        problems = run(paper(rows))
        self.assertTrue(any('"kick api": blocked needs a reason' in p for p in problems), problems)
        self.assertTrue(any('"tts": closed but names no section' in p for p in problems), problems)

    def test_not_checked_outside_the_ledger_fails(self):
        body = '## 1. Findings\n\nShield Mode bundle: not checked.\n\n'
        problems = run(paper(closed_rows(15), body=body))
        self.assertTrue(any('"not checked" outside the ledger' in p for p in problems), problems)

    def test_not_checked_inside_a_blocked_row_passes(self):
        rows = closed_rows(15) + '| shield api | outside | blocked | | Twitch page 403, not checked live |\n'
        self.assertEqual([], run(paper(rows)))

    def test_a_wrong_closed_count_fails(self):
        problems = run(paper(closed_rows(15), summary='Closed 20 of 20.\n\n'))
        self.assertTrue(any('rows say Closed 15 of 15' in p for p in problems), problems)


if __name__ == '__main__':
    unittest.main()
