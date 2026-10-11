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
# Tests for scripts/check-ia-placement.py.  Run: python scripts/check-ia-placement_test.py

import contextlib
import importlib.util
import io
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    'check_ia_placement', pathlib.Path(__file__).with_name('check-ia-placement.py'))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

HEADER = '| Today | Today group | New group | New page | New tab | Read | Manage | Slice |\n|---|---|---|---|---|---|---|---|\n'


def row(today, page, tabs, slice_id):
    return '| %s | G | G | %s | %s | Moderator | Moderator | %s |\n' % (today, page, tabs, slice_id)


LEDGER = (
    '# Ledger\n\n## Pages\n\n' + HEADER
    + row('Dashboard', 'Home', 'Streamer: Prep (offline); Viewer: My channels', '-')
    + row('ChatTriggers', 'Triggers & Timers', 'Chat triggers', '-')
    + row('Timers', 'Triggers & Timers', 'Timers', '-')
    + row('SoundClips', 'Library', 'Sounds', 'S-IA-LIBRARY')
    + row('CodeScripts', 'Scripts', 'the editor itself (no list page)', '-')
    + row('EventResponses', 'Event Responses', 'by event type (Alerts absorbed)', '-')
    + row('Federation', 'Federation', '- (nothing)', '-')
    + row('Participant: Me', 'Me', 'TTS voice, pronouns', 'S-IA-ROLE-HOMES')
    + '\n## Other\n\n| x |\n'
)
PLAN = '- **S-IA-LIBRARY** Library page.\n  continuation line\n'
CLEAN_PAGES = '''enum class ShellPage(val slug: String, val tabs: List<String>) {
    Home("home", emptyList()),
    Triggers("triggers-timers", listOf("chat-triggers", "timers")),
    Scripts("scripts", emptyList()),
    EventResponses("event-responses", emptyList()),
    Federation("federation", emptyList()),
}
'''


class IaPlacementGuard(unittest.TestCase):
    def run_check(self, pages, plan=PLAN, ledger=LEDGER):
        problems, _, _ = guard.check(ledger, plan, pages)
        return problems

    def test_clean_fixture_passes(self):
        self.assertEqual([], self.run_check(CLEAN_PAGES))

    def test_planted_unplaced_page_fails_naming_it(self):
        pages = CLEAN_PAGES.replace('}', '    Rogue("rogue-page", emptyList()),\n}')
        self.assertEqual(["ShellPage.kt page 'rogue-page' has no row in the placement ledger"], self.run_check(pages))

    def test_planted_unplaced_tab_fails_naming_it(self):
        pages = CLEAN_PAGES.replace('"timers")', '"timers", "extra-tab")')
        self.assertEqual(["ShellPage.kt tab 'triggers-timers/extra-tab' has no row in the placement ledger"],
                         self.run_check(pages))

    def test_closed_slice_row_without_a_page_fails_naming_row_and_slug(self):
        pages = CLEAN_PAGES.replace('    Federation("federation", emptyList()),\n', '')
        problems = self.run_check(pages)
        self.assertEqual(1, len(problems))
        self.assertIn("ledger row 'Federation'", problems[0])
        self.assertIn("'federation'", problems[0])

    def test_closed_slice_tab_without_a_tab_fails_naming_slug(self):
        pages = CLEAN_PAGES.replace('"chat-triggers", ', '')
        problems = self.run_check(pages)
        self.assertEqual(1, len(problems))
        self.assertIn("'triggers-timers/chat-triggers'", problems[0])

    def test_open_slice_row_without_a_page_passes(self):
        self.assertEqual([], self.run_check(CLEAN_PAGES))
        self.assertNotIn('library', guard.parse_pages(CLEAN_PAGES))

    def test_open_slice_row_becomes_required_when_the_plan_entry_is_deleted(self):
        problems = self.run_check(CLEAN_PAGES, plan='- **S-OTHER** x\n')
        self.assertEqual(1, len(problems))
        self.assertIn("page 'library'", problems[0])

    def test_open_slice_page_present_in_app_passes(self):
        pages = CLEAN_PAGES.replace('}', '    Library("library", listOf("sounds")),\n}')
        self.assertEqual([], self.run_check(pages))

    def test_viewer_shell_rows_are_not_required(self):
        problems = self.run_check(CLEAN_PAGES, plan='')
        self.assertFalse([p for p in problems if "'Participant" in p])

    def test_mode_cells_are_not_tabs(self):
        for cell in ('Streamer: Prep (offline); Viewer: My channels', 'by event type (Alerts absorbed)',
                     'the editor itself (no list page)', '- (generic webhooks only)', '23 tabs unchanged; entering swaps'):
            self.assertEqual([], guard.split_tabs(cell), cell)

    def test_real_tabs_are_split_and_slugged(self):
        tabs = guard.split_tabs('Settings; Language & fonts (Billing in saas mode only); One channel (Shield)')
        self.assertEqual(['settings', 'language-fonts', 'one-channel'], [guard.slug(tab) for tab in tabs])

    def test_slug_rule(self):
        self.assertEqual('triggers-timers', guard.slug('Triggers & Timers'))
        self.assertEqual('chat-triggers', guard.slug('Chat triggers'))
        self.assertEqual('api-devices', guard.slug('API & devices'))

    def test_main_reports_one_line_per_problem_and_exit_codes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            (root / 'ledger.md').write_text(LEDGER, encoding='utf-8')
            (root / 'plan.md').write_text(PLAN, encoding='utf-8')
            (root / 'pages.kt').write_text(CLEAN_PAGES, encoding='utf-8')
            args = ['--ledger', str(root / 'ledger.md'), '--plan', str(root / 'plan.md'), '--pages', str(root / 'pages.kt')]
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(0, guard.main(args))
            self.assertEqual('check-ia-placement: 5 page(s), 2 tab(s) placed, 0 problem(s)\n', out.getvalue())
            (root / 'pages.kt').write_text(CLEAN_PAGES.replace('}', '    Rogue("rogue", emptyList()),\n}'), encoding='utf-8')
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(1, guard.main(args))
            self.assertEqual(["ShellPage.kt page 'rogue' has no row in the placement ledger",
                              'check-ia-placement: 1 problem(s)'], out.getvalue().splitlines())

    def test_missing_pages_file_fails(self):
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, guard.main(['--pages', 'does-not-exist.kt']))


if __name__ == '__main__':
    unittest.main()
