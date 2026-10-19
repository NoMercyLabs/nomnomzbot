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
# Tests for scripts/check-no-speech-synthesis.py.  Run: python scripts/check-no-speech-synthesis_test.py

import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    'check_no_speech_synthesis', pathlib.Path(__file__).with_name('check-no-speech-synthesis.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class NoSpeechSynthesisGate(unittest.TestCase):
    def test_the_overlay_sdk_calling_speech_synthesis_fails(self):
        files = {'server/src/NomNomzBot.Api/Controllers/OverlaySdkController.cs':
                 'var synth = window.speechSynthesis;\nsynth.speak(u);'}
        self.assertEqual([('server/src/NomNomzBot.Api/Controllers/OverlaySdkController.cs', 1,
                           'var synth = window.speechSynthesis;')], gate.find_hits(files))

    def test_a_dashboard_utterance_fails(self):
        files = {'dashboard/src/views/Tts.vue': 'const u = new SpeechSynthesisUtterance(text)'}
        self.assertEqual(1, len(gate.find_hits(files)))

    def test_a_compose_source_fails(self):
        files = {'app/composeApp/src/wasmJsMain/kotlin/Speak.kt': 'js("window.speechSynthesis.speak(u)")'}
        self.assertEqual(1, len(gate.find_hits(files)))

    def test_clean_source_and_out_of_scope_files_pass(self):
        files = {'server/src/A.cs': 'class A {}',
                 'server/tests/B.cs': 'sdk.Should().NotContain("speechSynthesis");',
                 'dashboard/node_modules/x/index.js': 'speechSynthesis',
                 'app/composeApp/build/out.js': 'speechSynthesis'}
        self.assertEqual([], gate.find_hits(files))


if __name__ == '__main__':
    unittest.main()
