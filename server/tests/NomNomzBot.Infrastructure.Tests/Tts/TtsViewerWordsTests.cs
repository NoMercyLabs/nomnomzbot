// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the bot/viewer split of one spoken line: the announcement stays the bot's, what the chatter wrote
/// becomes the chatter's own part, and the quote marks that only frame it are not spoken.
/// </summary>
public sealed class TtsViewerWordsTests
{
    private const string ViewerId = "viewer-1";

    [Fact]
    public void A_marked_line_splits_into_a_bot_part_and_the_viewers_own_part()
    {
        Dictionary<string, string> marked = TtsViewerWords.Mark(
            new Dictionary<string, string> { ["user"] = "iFillz", ["message"] = "hype train" }
        );

        List<TtsSpeakSegment>? parts = TtsViewerWords.Split(
            $"iFillz resubscribed for 55 months! They said: {marked["message"]}",
            ViewerId
        );

        parts.Should().NotBeNull();
        parts!
            .Select(p => (p.Text, p.Speaker, p.SpeakerTwitchUserId))
            .Should()
            .Equal(
                ("iFillz resubscribed for 55 months! They said:", TtsSpeaker.Bot, null),
                ("hype train", TtsSpeaker.Viewer, ViewerId)
            );
    }

    [Fact]
    public void Quote_marks_that_only_frame_the_viewers_words_are_not_spoken()
    {
        Dictionary<string, string> marked = TtsViewerWords.Mark(
            new Dictionary<string, string>
            {
                ["message"] = "hi there",
                ["also_said"] = " They also said: \"hi there\"",
            }
        );

        List<TtsSpeakSegment>? parts = TtsViewerWords.Split(
            $"Thanks!{marked["also_said"]}",
            ViewerId
        );

        parts!.Select(p => p.Text).Should().Equal("Thanks! They also said:", "hi there");
    }

    [Fact]
    public void A_line_the_viewer_wrote_nothing_in_has_no_viewer_part()
    {
        Dictionary<string, string> variables = new() { ["user"] = "iFillz", ["message"] = "  " };

        TtsViewerWords.Words(variables).Should().BeEmpty();
        TtsViewerWords.Split("iFillz subscribed!", ViewerId).Should().BeNull();
    }

    [Fact]
    public void A_rendered_line_without_marks_finds_the_viewers_words_as_a_whole_word()
    {
        List<TtsSpeakSegment>? parts = TtsViewerWords.SplitRendered(
            "a streak of 3: a",
            new Dictionary<string, string> { ["message"] = "a" },
            ViewerId
        );

        parts!
            .Select(p => (p.Text, p.Speaker))
            .Should()
            .Equal(("a streak of 3:", TtsSpeaker.Bot), ("a", TtsSpeaker.Viewer));
    }
}
