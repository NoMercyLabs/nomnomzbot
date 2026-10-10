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
# Tests for scripts/check-plain-english.py.  Run: python scripts/check-plain-english_test.py

import importlib.util
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    'check_plain_english', pathlib.Path(__file__).with_name('check-plain-english.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

FRONT = (
    '# Community safety (2026-10-10)\n\n'
    'Audience: streamers and moderators.\n'
    'Owner: NoMercy Labs.\n'
    'Date: 2026-10-10.\n\n'
    '## Contents\n\n- 1. Goals\n\n'
    '## Glossary\n\n| Word | Meaning here |\n|---|---|\n| TTS | Text to speech. |\n\n'
)


def run(text, fragment=False, share=False):
    with tempfile.TemporaryDirectory() as tmp:
        path = pathlib.Path(tmp) / 'community-safety.md'
        path.write_text(text, encoding='utf-8')
        return gate.check_file(path, fragment=fragment, share=share)


class CheckPlainEnglishTests(unittest.TestCase):
    def test_a_plain_paper_passes(self):
        self.assertEqual([], run(FRONT + '## 1. Goals\n\nThe bot reads chat. TTS speaks the line.\n', share=True))

    def test_a_long_sentence_fails(self):
        long_line = ' '.join(['word'] * 21) + '.'
        problems = run(FRONT + '## 1. Goals\n\n' + long_line + '\n')
        self.assertTrue(any('21 words' in p for p in problems), problems)

    def test_quotes_and_links_do_not_count(self):
        quote = '"' + ' '.join(['said'] * 30) + '"'
        line = f'A study found {quote} in [one paper](https://example.com/a/b/c).'
        self.assertEqual([], run(FRONT + '## 1. Goals\n\n' + line + '\n'))

    def test_table_cells_count_as_sentences(self):
        cell = ' '.join(['cell'] * 22) + '.'
        problems = run(FRONT + '## 1. Goals\n\n| A | B |\n|---|---|\n| x | ' + cell + ' |\n')
        self.assertTrue(any('22 words' in p for p in problems), problems)

    def test_a_complex_word_fails_with_its_plain_word(self):
        problems = run(FRONT + '## 1. Goals\n\nWe utilize the token.\n')
        self.assertTrue(any('"utilize"' in p and '"use"' in p for p in problems), problems)

    def test_a_full_paper_needs_its_front_matter(self):
        problems = run('# Community safety\n\n## 1. Goals\n\nThe bot reads chat.\n')
        for part in ('Audience:', 'Owner:', 'Date:', '## Contents', 'Glossary'):
            self.assertTrue(any(part in p for p in problems), (part, problems))

    def test_a_fragment_skips_the_front_matter(self):
        self.assertEqual([], run('## 7. Goals\n\nThe bot reads chat.\n', fragment=True))

    def test_an_acronym_outside_the_glossary_fails(self):
        problems = run(FRONT + '## 1. Goals\n\nThe MFA step stops a thief.\n')
        self.assertTrue(any('"MFA"' in p for p in problems), problems)

    def test_a_shared_paper_has_no_file_references(self):
        text = FRONT + '## 1. Goals\n\nThe ban lives in ModerationService.cs:131 today.\n'
        problems = run(text, share=True)
        self.assertTrue(any('file reference' in p for p in problems), problems)

    def test_a_shared_paper_has_no_slice_ids_or_code(self):
        text = FRONT + '## 1. Goals\n\nS-MOD-DECISION-CARD adds a card. Call `BanAsync` first.\n'
        problems = run(text, share=True)
        self.assertTrue(any('slice ID' in p for p in problems), problems)
        self.assertTrue(any('code' in p for p in problems), problems)

    def test_a_shared_paper_has_no_internal_wording(self):
        text = FRONT + '## 1. Goals\n\nStoney wrote this in the session. See the PLAN.\n'
        problems = run(text, share=True)
        self.assertTrue(any('internal wording' in p for p in problems), problems)

    def test_slice_ids_are_not_acronyms(self):
        text = FRONT + '## 1. Goals\n\nS-MOD-DECISION-CARD adds a card.\n'
        self.assertEqual([], run(text, share=False))

    def test_internal_papers_may_cite_files(self):
        text = FRONT + '## 1. Goals\n\nThe ban lives in ModerationService.cs:131 today.\n'
        self.assertEqual([], run(text, share=False))


if __name__ == '__main__':
    unittest.main()
