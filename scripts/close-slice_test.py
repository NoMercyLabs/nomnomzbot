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
# Tests for scripts/close-slice.ps1. Each case copies the script into a throwaway repo with its own
# plan file, closes one slice, and reads back the committed plan. Run: python scripts/close-slice_test.py

import pathlib
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parent / 'close-slice.ps1'
PLAN = '.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md'


class PlanRepo:
    def __init__(self, root: pathlib.Path, plan: str):
        self.root = root
        self.git('init', '-q', '-b', 'master')
        self.git('config', 'user.name', 'test')
        self.git('config', 'user.email', 'test@example.com')
        self.git('config', 'core.autocrlf', 'false')
        (root / 'scripts').mkdir()
        shutil.copy(SCRIPT, root / 'scripts' / 'close-slice.ps1')
        plan_path = root / PLAN
        plan_path.parent.mkdir(parents=True)
        plan_path.write_text(plan, encoding='utf-8')
        self.git('add', '--', PLAN)
        self.git('commit', '-q', '-m', 'docs: plan')

    def git(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(['git', *args], cwd=self.root, capture_output=True, text=True, check=True)

    def close(self, slice_id: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
             str(self.root / 'scripts' / 'close-slice.ps1'), '-Slice', slice_id, '-Message', 'shipped'],
            cwd=self.root, capture_output=True, text=True)

    def committed_plan(self) -> list[str]:
        return self.git('show', f'HEAD:{PLAN}').stdout.splitlines()


class CloseSliceTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self._tmp.cleanup()

    def repo(self, plan: str) -> PlanRepo:
        return PlanRepo(pathlib.Path(self._tmp.name), plan)

    def test_a_checkbox_entry_closes_and_its_checkbox_siblings_stay(self):
        repo = self.repo(
            '### S-SPAM\n'
            '- [ ] **S-A** first\n'
            '  more of A\n'
            '- [ ] **S-B** second\n'
            '- [ ] **S-C** third\n')

        result = repo.close('S-B')

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(repo.committed_plan(), [
            '### S-SPAM',
            '- [ ] **S-A** first',
            '  more of A',
            '- [ ] **S-C** third',
        ])

    def test_a_plain_entry_before_a_checkbox_sibling_leaves_the_sibling(self):
        repo = self.repo(
            '- **S-A** plain\n'
            '  more of A\n'
            '- [ ] **S-B** checkbox\n')

        result = repo.close('S-A')

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(repo.committed_plan(), ['- [ ] **S-B** checkbox'])

    def test_the_last_entry_of_a_section_leaves_the_next_subheading(self):
        repo = self.repo(
            '### First\n'
            '- **S-A** only entry\n'
            '\n'
            '### Second\n'
            '- **S-B** next\n')

        result = repo.close('S-A')

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(repo.committed_plan(), ['### First', '', '### Second', '- **S-B** next'])

    def test_an_id_that_prefixes_another_closes_only_itself(self):
        repo = self.repo('- **S006** one\n- **S006b** two\n')

        result = repo.close('S006')

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(repo.committed_plan(), ['- **S006b** two'])


if __name__ == '__main__':
    unittest.main()
