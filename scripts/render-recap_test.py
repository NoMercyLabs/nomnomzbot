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
# Tests for scripts/render-recap.py (offline, through --payload-file; renders in real headless Chrome).
# Run: python scripts/render-recap_test.py

import contextlib
import copy
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "render_recap", pathlib.Path(__file__).with_name("render-recap.py"))
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)

# A 1x1 PNG, so the avatar loads without the network.
AVATAR = ("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
LABELS = {"title": "Stream recap", "kpi.duration": "Time live", "kpi.peak": "Peak viewers", "kpi.messages": "Chat messages",
          "block.chatters": "Top chatters", "chatters.unit.one": "chatter", "chatters.unit.other": "chatters"}

# Stream started 2026-10-15 in Amsterdam: day 15, so --base must be 14 * 360 / 31 = 162.58 degrees.
GOOD = {
    "streamId": "STREAM-ALPHA",
    "labels": LABELS,
    "stats": {
        "stream": {"id": "STREAM-ALPHA", "startedAt": "2026-10-15T10:00:00+00:00", "endedAt": "2026-10-15T14:00:00+00:00",
                   "title": "Alpha stream", "game": "Alpha game"},
        "channel": {"name": "AlphaChannel", "avatarUrl": AVATAR},
        "duration": 14400, "peakViewers": 21, "followers": 2,
        "chat": {"messages": 90, "uniqueChatters": 4},
        "topChatters": [{"name": "viewer_one", "count": 40}, {"name": "viewer_two", "count": 25}],
    },
}


def run(dto, stream=None):
    with tempfile.TemporaryDirectory() as work:
        source = pathlib.Path(work) / "recap.json"
        source.write_text(json.dumps(dto), encoding="utf-8")
        out = pathlib.Path(work) / "out.png"
        args = ["--payload-file", str(source), "--out", str(out)]
        if stream:
            args += ["--stream", stream]
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            code = tool.main(args)
        return code, printed.getvalue(), out.exists()


class RenderRecap(unittest.TestCase):
    def test_whole_payload_passes_and_writes_the_png(self):
        code, text, written = run(GOOD)
        self.assertEqual((0, True), (code, written), text)

    def test_payload_without_avatar_exits_1_and_writes_nothing(self):
        dto = copy.deepcopy(GOOD)
        del dto["stats"]["channel"]["avatarUrl"]
        code, text, written = run(dto)
        self.assertEqual(1, code)
        self.assertFalse(written)
        self.assertIn("FAIL channel avatar", text)
        self.assertIn("FAIL watermark", text)

    def test_payload_without_channel_names_both_failures(self):
        dto = copy.deepcopy(GOOD)
        del dto["stats"]["channel"]
        code, text, written = run(dto)
        self.assertEqual((1, False), (code, written))
        self.assertIn("FAIL channel name", text)

    def test_stream_id_mismatch_fails(self):
        code, text, written = run(GOOD, stream="STREAM-OTHER")
        self.assertEqual((1, False), (code, written))
        self.assertIn("FAIL stream id", text)

    def test_gallery_sample_values_fail(self):
        dto = copy.deepcopy(GOOD)
        dto["stats"]["topChatters"][0]["name"] = "TestChatter"
        code, text, written = run(dto)
        self.assertEqual((1, False), (code, written))
        self.assertIn("FAIL sample data", text)

    def test_installed_settings_without_labels_still_render_every_word(self):
        dto = copy.deepcopy(GOOD)
        dto["labels"] = {}
        dto["stats"]["chat"]["messages"] = 1107
        code, text, written = run(dto)
        self.assertEqual((0, True), (code, written), text)

    def test_unit_word_missing_fails(self):
        facts = {"bigs": [{"block": "chatters", "big": "29", "unit": ""}, {"block": "raids", "big": "1", "unit": ""},
                          {"block": "songs", "big": "106", "unit": ""}, {"block": "bits", "big": "200", "unit": ""}]}
        found = tool.word_checks({}, facts)
        self.assertEqual(3, len(found), found)
        self.assertTrue(all(line.startswith("FAIL unit word") for line in found))

    def test_raid_card_needs_both_unit_words(self):
        facts = {"bigs": [{"block": "raids", "big": "1", "unit": "raid"}]}
        self.assertEqual(1, len(tool.word_checks({}, facts)))
        facts = {"bigs": [{"block": "raids", "big": "1", "unit": "raid · 11 viewers"}]}
        self.assertEqual([], tool.word_checks({}, facts))

    def test_dutch_grouping_fails_and_en_us_passes(self):
        stats = {"chat": {"messages": 1107}}
        bad = tool.word_checks(stats, {"numbers": ["1.107"]})
        self.assertEqual(2, len(bad), bad)
        self.assertEqual([], tool.word_checks(stats, {"numbers": ["1,107"]}))

    def test_missing_thank_you_line_fails_only_when_the_payload_has_a_duration(self):
        self.assertEqual(1, len(tool.word_checks({"duration": 32400}, {"thanks": None})))
        self.assertEqual([], tool.word_checks({"duration": 32400}, {"thanks": "Thank you for 9 hours of coding."}))
        self.assertEqual([], tool.word_checks({}, {"thanks": None}))

    def test_empty_label_and_unfilled_placeholder_fail(self):
        found = tool.word_checks({}, {"emptyLabels": ["chatters:label"], "thanks": "Thank you for {duration}"})
        self.assertEqual(2, len(found), found)

    def test_day_hue_follows_the_amsterdam_day(self):
        # 23:30 UTC on the 14th is already the 15th in Amsterdam (CEST).
        self.assertAlmostEqual(14 * 360 / 31, tool.expected_hue("2026-10-14T23:30:00+00:00"), places=6)
        self.assertAlmostEqual(13 * 360 / 31, tool.expected_hue("2026-10-14T21:30:00+00:00"), places=6)


if __name__ == "__main__":
    unittest.main()
