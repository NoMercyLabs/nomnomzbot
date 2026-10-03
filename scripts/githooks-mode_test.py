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
# Git skips a hook that is not executable, and on Windows the working tree has no mode bit to show it,
# so a hook committed as 100644 runs here and never on a Linux or macOS clone. Every file in .githooks
# named after a git hook must be committed as 100755. Run: python scripts/githooks-mode_test.py

import pathlib
import subprocess
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
GIT_HOOK_NAMES = {
    'applypatch-msg', 'pre-applypatch', 'post-applypatch', 'pre-commit', 'pre-merge-commit',
    'prepare-commit-msg', 'commit-msg', 'post-commit', 'pre-rebase', 'post-checkout', 'post-merge',
    'pre-push', 'pre-receive', 'update', 'proc-receive', 'post-receive', 'post-update',
    'reference-transaction', 'push-to-checkout', 'pre-auto-gc', 'post-rewrite', 'sendemail-validate',
    'fsmonitor-watchman', 'p4-changelist', 'p4-prepare-changelist', 'p4-post-changelist', 'p4-pre-submit',
    'post-index-change',
}


def index_modes() -> dict[str, str]:
    out = subprocess.run(['git', 'ls-files', '-s', '--', '.githooks'], cwd=ROOT, capture_output=True,
                         text=True, check=True).stdout
    modes = {}
    for line in out.splitlines():
        meta, path = line.split('\t', 1)
        modes[path] = meta.split()[0]
    return modes


class GitHooksModeTests(unittest.TestCase):
    def test_every_git_hook_is_committed_executable(self):
        modes = index_modes()
        hooks = {p: m for p, m in modes.items() if pathlib.PurePosixPath(p).name in GIT_HOOK_NAMES}
        self.assertTrue(hooks, 'no git hooks found in .githooks')
        not_executable = sorted(p for p, m in hooks.items() if m != '100755')
        self.assertEqual(not_executable, [], 'fix with: git update-index --chmod=+x -- <path>')


if __name__ == '__main__':
    unittest.main()
