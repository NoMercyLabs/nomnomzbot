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
# Tests for the amend guard in .githooks/prepare-commit-msg. Each case builds a throwaway repo that
# uses this repo's .githooks. Run: python scripts/amend-guard_test.py

import pathlib
import subprocess
import tempfile
import unittest

HOOKS = pathlib.Path(__file__).resolve().parent.parent / '.githooks'


class Repo:
    def __init__(self, root: pathlib.Path):
        self.root = root
        self.git('init', '-q', '-b', 'master')
        self.git('config', 'user.name', 'test')
        self.git('config', 'user.email', 'test@example.com')
        self.git('config', 'core.autocrlf', 'false')
        self.git('config', 'core.hooksPath', HOOKS.as_posix())

    def git(self, *args: str, check: bool = True) -> subprocess.CompletedProcess:
        return subprocess.run(['git', *args], cwd=self.root, capture_output=True, text=True, check=check)

    def write(self, name: str, text: str) -> None:
        (self.root / name).write_text(text, encoding='utf-8')

    def commit_file(self, name: str, text: str, message: str) -> None:
        self.write(name, text)
        self.git('add', '--', name)
        self.git('commit', '-q', '-m', message)

    def head(self) -> str:
        return self.git('rev-parse', 'HEAD').stdout.strip()

    def head_files(self) -> list[str]:
        return sorted(self.git('show', '--name-only', '--format=', 'HEAD').stdout.split())

    def staged(self) -> list[str]:
        return sorted(self.git('diff', '--cached', '--name-only').stdout.split())


class AmendGuardTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.repo = Repo(pathlib.Path(self._tmp.name))
        self.repo.commit_file('base.txt', 'base\n', 'chore: base')
        self.repo.commit_file('mine.txt', 'mine\n', 'feat: mine')

    def tearDown(self):
        self._tmp.cleanup()

    def stage_foreign_file(self) -> None:
        self.repo.write('foreign.txt', 'someone else\n')
        self.repo.git('add', '--', 'foreign.txt')

    def test_amend_with_a_foreign_staged_file_is_refused_and_leaves_head_and_index_alone(self):
        self.stage_foreign_file()
        before = self.repo.head()
        result = self.repo.git('commit', '--amend', '--no-edit', check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('foreign.txt', result.stderr)
        self.assertIn('--only', result.stderr)
        self.assertEqual(self.repo.head(), before)
        self.assertEqual(self.repo.head_files(), ['mine.txt'])
        self.assertEqual(self.repo.staged(), ['foreign.txt'])

    def test_no_verify_does_not_skip_the_guard(self):
        self.stage_foreign_file()
        before = self.repo.head()
        result = self.repo.git('commit', '--amend', '--no-edit', '--no-verify', check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)

    def test_commit_all_amend_that_would_sweep_a_foreign_change_is_refused(self):
        self.repo.commit_file('foreign.txt', 'v1\n', 'feat: foreign')
        self.repo.commit_file('mine.txt', 'mine v2\n', 'feat: mine again')
        self.repo.write('foreign.txt', 'v2\n')
        before = self.repo.head()
        result = self.repo.git('commit', '--amend', '--no-edit', '-a', check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)

    def test_reword_with_only_keeps_the_tree_and_the_foreign_file_staged(self):
        self.stage_foreign_file()
        tree_before = self.repo.git('rev-parse', 'HEAD^{tree}').stdout.strip()
        result = self.repo.git('commit', '--amend', '--only', '-m', 'feat: mine, reworded', check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.repo.git('rev-parse', 'HEAD^{tree}').stdout.strip(), tree_before)
        self.assertEqual(self.repo.git('log', '-1', '--format=%s').stdout.strip(), 'feat: mine, reworded')
        self.assertEqual(self.repo.staged(), ['foreign.txt'])

    def test_amend_that_only_restages_the_commits_own_file_passes(self):
        self.repo.write('mine.txt', 'mine, fixed\n')
        self.repo.git('add', '--', 'mine.txt')
        result = self.repo.git('commit', '--amend', '--no-edit', check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.repo.git('show', 'HEAD:mine.txt').stdout, 'mine, fixed\n')
        self.assertEqual(self.repo.git('rev-list', '--count', 'HEAD').stdout.strip(), '2')

    def test_amend_with_a_clean_index_passes(self):
        result = self.repo.git('commit', '--amend', '-m', 'feat: mine, renamed', check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.repo.git('log', '-1', '--format=%s').stdout.strip(), 'feat: mine, renamed')

    def test_amend_of_the_root_commit_with_a_foreign_staged_file_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Repo(pathlib.Path(tmp))
            repo.commit_file('first.txt', 'first\n', 'chore: first')
            repo.write('foreign.txt', 'x\n')
            repo.git('add', '--', 'foreign.txt')
            result = repo.git('commit', '--amend', '--no-edit', check=False)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(repo.head_files(), ['first.txt'])

    def test_a_normal_commit_with_staged_files_is_not_touched(self):
        self.stage_foreign_file()
        result = self.repo.git('commit', '-m', 'feat: foreign', check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.repo.head_files(), ['foreign.txt'])


if __name__ == '__main__':
    unittest.main()
