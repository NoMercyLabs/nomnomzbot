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
# Tests for check-sdk-docs.py. Run: python scripts/check-sdk-docs_test.py

import importlib.util
import pathlib
import tempfile
import unittest

_spec = importlib.util.spec_from_file_location(
    'check_sdk_docs', pathlib.Path(__file__).resolve().parent / 'check-sdk-docs.py')
check_sdk_docs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(check_sdk_docs)

PAGE = '<!-- slices: S-DONE -->\n# Send a chat message\n\n`chat.send(text)` posts text in chat.\n'
PLAN = '# Plan\n\n- **S-OPEN** Still open.\n  Done-when: later.\n'


def audit_for(page_text: str, verdict: str = 'true', path: str = 'scripts/send.md') -> str:
    return (f'## {path}\n<!-- audited sha256:{check_sdk_docs.page_hash(page_text)} -->\n'
            '| Claim | Verdict | Evidence |\n|---|---|---|\n'
            f'| `chat.send` posts text | {verdict} | JintScriptExecutor.cs:165 |\n')


class CheckSdkDocsTests(unittest.TestCase):
    def run_check(self, page: str, audit: str, plan: str = PLAN) -> list[str]:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / 'docs' / 'scripts').mkdir(parents=True)
            (root / 'docs' / 'scripts' / 'send.md').write_text(page, encoding='utf-8')
            (root / 'audit.md').write_text(audit, encoding='utf-8')
            (root / 'plan.md').write_text(plan, encoding='utf-8')
            return check_sdk_docs.check(root / 'docs', root / 'audit.md', root / 'plan.md')

    def test_a_fully_verified_page_on_a_closed_slice_passes(self):
        self.assertEqual(self.run_check(PAGE, audit_for(PAGE)), [])

    def test_a_stale_claim_fails_and_names_it(self):
        problems = self.run_check(PAGE, audit_for(PAGE, verdict='stale'))
        self.assertEqual(len(problems), 1)
        self.assertIn('scripts/send.md', problems[0])
        self.assertIn('`chat.send` posts text', problems[0])
        self.assertIn('stale', problems[0])

    def test_a_wrong_or_unchecked_claim_fails(self):
        self.assertEqual(len(self.run_check(PAGE, audit_for(PAGE, verdict='wrong'))), 1)
        self.assertEqual(len(self.run_check(PAGE, audit_for(PAGE, verdict='not checked'))), 1)

    def test_an_audit_whose_checker_wrote_fail_fails_even_with_all_rows_true(self):
        audit = audit_for(PAGE).replace(' -->\n', ' -->\nVerdict: FAIL\n', 1)
        problems = self.run_check(PAGE, audit)
        self.assertEqual(len(problems), 1)
        self.assertIn('Verdict: FAIL', problems[0])

    def test_a_row_with_a_pipe_inside_its_claim_fails_instead_of_being_skipped(self):
        audit = audit_for(PAGE) + '| `a || b` picks b | true | JintScriptExecutor.cs:165 |\n'
        problems = self.run_check(PAGE, audit)
        self.assertEqual(len(problems), 1)
        self.assertIn('a || b', problems[0])

    def test_an_escaped_pipe_inside_a_cell_is_part_of_the_cell(self):
        audit = audit_for(PAGE) + '| pick gives `T \\| undefined` | true | SdkRuntimeSurface.cs:549 |\n'
        self.assertEqual(self.run_check(PAGE, audit), [])

    def test_a_page_with_no_audit_section_fails(self):
        problems = self.run_check(PAGE, '## scripts/other.md\n')
        self.assertEqual(len(problems), 1)
        self.assertIn('no audit section', problems[0])

    def test_a_page_edited_after_its_audit_fails(self):
        edited = PAGE + '\nA new sentence nobody checked.\n'
        problems = self.run_check(edited, audit_for(PAGE))
        self.assertEqual(len(problems), 1)
        self.assertIn('changed since its audit', problems[0])

    def test_a_page_describing_an_open_slice_fails(self):
        page = PAGE.replace('S-DONE', 'S-OPEN')
        problems = self.run_check(page, audit_for(page))
        self.assertEqual(len(problems), 1)
        self.assertIn('S-OPEN', problems[0])
        self.assertIn('still open', problems[0])

    def test_a_page_that_declares_no_slices_fails(self):
        page = PAGE.replace('<!-- slices: S-DONE -->\n', '')
        problems = self.run_check(page, audit_for(page))
        self.assertEqual(len(problems), 1)
        self.assertIn('slices', problems[0])

    def test_a_page_on_no_slice_passes_when_declared_none(self):
        page = PAGE.replace('S-DONE', 'none')
        self.assertEqual(self.run_check(page, audit_for(page)), [])

    def test_line_endings_do_not_change_the_hash(self):
        self.assertEqual(check_sdk_docs.page_hash('a\r\nb\r\n'), check_sdk_docs.page_hash('a\nb\n'))


if __name__ == '__main__':
    unittest.main()
