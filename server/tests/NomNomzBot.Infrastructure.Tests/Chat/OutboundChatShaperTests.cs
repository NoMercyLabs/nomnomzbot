// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves S010's outbound shaping: per-platform word-boundary chunking that loses no text, the
/// <see cref="BotEmittedLine"/> loop-guard marker surviving chunking without counting against the
/// visible-character budget, and the trailing invisible variation on a verbatim-repeated line.
/// </summary>
public sealed class OutboundChatShaperTests
{
    private const string QueueKey = "channel-1:twitch";

    [Fact]
    public void A_900_character_twitch_line_chunks_within_500_with_no_text_lost()
    {
        OutboundChatShaper shaper = new();
        string original = string.Join(" ", Enumerable.Range(1, 130).Select(i => $"word{i}")); // well over 500 chars, plain words only

        IReadOnlyList<string> chunks = shaper.Shape("twitch", QueueKey, original);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 500));
        Assert.Equal(original, string.Join(" ", chunks));
    }

    [Fact]
    public void The_same_line_chunks_at_200_for_youtube_not_the_twitch_limit()
    {
        OutboundChatShaper shaper = new();
        string original = string.Join(" ", Enumerable.Range(1, 130).Select(i => $"word{i}"));

        IReadOnlyList<string> twitchChunks = shaper.Shape("twitch", QueueKey, original);
        IReadOnlyList<string> youtubeChunks = shaper.Shape(
            "youtube",
            "channel-1:youtube",
            original
        );

        Assert.All(twitchChunks, c => Assert.True(c.Length <= 500));
        Assert.All(youtubeChunks, c => Assert.True(c.Length <= 200));
        Assert.True(youtubeChunks.Count > twitchChunks.Count);
        Assert.Equal(original, string.Join(" ", youtubeChunks));
    }

    [Fact]
    public void Chunking_never_splits_a_single_word_when_a_boundary_exists()
    {
        OutboundChatShaper shaper = new();
        // 60-char words either side of the boundary — the split must land on the space between them.
        string longWord1 = new('a', 60);
        string longWord2 = new('b', 60);
        string text = string.Join(" ", Enumerable.Repeat(longWord1, 5).Append(longWord2));

        IReadOnlyList<string> chunks = shaper.Shape("youtube", QueueKey, text); // limit 200

        foreach (string chunk in chunks)
        {
            Assert.DoesNotContain(longWord1[..30] + longWord2[..10], chunk); // never glued mid-word
        }
        Assert.Equal(text, string.Join(" ", chunks));
    }

    [Fact]
    public void The_bot_emitted_marker_rides_only_the_first_chunk_and_is_not_counted_against_the_budget()
    {
        OutboundChatShaper shaper = new();
        string body = string.Join(" ", Enumerable.Range(1, 130).Select(i => $"word{i}"));
        string stamped = BotEmittedLine.Stamp(body);

        IReadOnlyList<string> chunks = shaper.Shape("twitch", QueueKey, stamped);

        Assert.StartsWith(BotEmittedLine.Marker, chunks[0]);
        Assert.True(BotEmittedLine.IsMarked(chunks[0]));
        Assert.All(chunks.Skip(1), c => Assert.False(BotEmittedLine.IsMarked(c)));
        // Stripping the marker back out reproduces the exact original body — it was never truncated
        // to make room for the (1-character) marker.
        string reassembled = string.Join(
            " ",
            chunks.Select((c, i) => i == 0 ? c[BotEmittedLine.Marker.Length..] : c)
        );
        Assert.Equal(body, reassembled);
        Assert.All(chunks, c => Assert.True(c.Replace(BotEmittedLine.Marker, "").Length <= 500));
    }

    [Fact]
    public void A_verbatim_repeat_on_the_same_queue_key_is_varied_so_it_still_posts()
    {
        OutboundChatShaper shaper = new();

        IReadOnlyList<string> first = shaper.Shape("twitch", QueueKey, "gg well played");
        IReadOnlyList<string> second = shaper.Shape("twitch", QueueKey, "gg well played");

        Assert.Equal("gg well played", first[0]);
        Assert.NotEqual(first[0], second[0]);
        // The variation is invisible — the human-visible text is unchanged.
        Assert.Equal("gg well played", second[0].Replace(OutboundChatShaper.VariationMarker, ""));
    }

    [Fact]
    public void A_repeat_on_a_different_queue_key_is_not_varied()
    {
        OutboundChatShaper shaper = new();

        shaper.Shape("twitch", "channel-A:twitch", "gg well played");
        IReadOnlyList<string> onOtherChannel = shaper.Shape(
            "twitch",
            "channel-B:twitch",
            "gg well played"
        );

        Assert.Equal("gg well played", onOtherChannel[0]);
    }

    [Fact]
    public void Three_consecutive_identical_lines_each_differ_from_their_immediate_predecessor()
    {
        OutboundChatShaper shaper = new();

        string first = shaper.Shape("twitch", QueueKey, "same line")[0];
        string second = shaper.Shape("twitch", QueueKey, "same line")[0];
        string third = shaper.Shape("twitch", QueueKey, "same line")[0];

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
    }
}
