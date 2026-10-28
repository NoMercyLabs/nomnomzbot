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
        Assert.All(chunks, c => Assert.True(c.Length <= 500));
    }

    private static readonly string[] ViewerQueue =
    [
        "#1 Sandstorm by Darude (in ~2 min)",
        "#2 Blue (Da Ba Dee) by Eiffel 65 (in ~5 min)",
        "#3 Rapture by Pendulum, Dr. Peacock (in ~8 min)",
        "#4 Wild Wild West by Da Tweekaz (in ~11 min)",
        "#5 Jägermeister by Da Tweekaz (in ~15 min)",
        "#6 Smile by SMiLE.dk (in ~17 min)",
        "#7 Halibo by Billx, Dr. Peacock (in ~20 min)",
        "#8 Headhunterz Dragonborn by Headhunterz (in ~24 min)",
        "#9 Wildstylez Year of Summer by Wildstylez, Niels Geusebroek (in ~27 min)",
        "#10 Kernkraft 400 by Zombie Nation (in ~30 min)",
        "#11 Epic Sax Guy by Sergey Stepanov (in ~33 min)",
        "#12 Rave in the Grave by Da Tweekaz, Frequencerz (in ~36 min)",
        "#13 Teenie Weenie by Brojob (in ~45 min)",
        "#14 Demolisher by Slaughter to Prevail (in ~48 min)",
        "#15 Hardcore Vibes by Dune (in ~51 min)",
    ];

    [Fact]
    public void A_long_queue_reply_splits_only_between_entries_and_every_sent_chunk_fits_500()
    {
        OutboundChatShaper shaper = new();
        string body =
            "@finwo_: "
            + string.Join(" | ", ViewerQueue.Concat(ViewerQueue.Select(e => e + " again")));
        string stamped = BotEmittedLine.Stamp(body);

        IReadOnlyList<string> chunks = shaper.Shape("twitch", QueueKey, stamped);

        Assert.True(chunks.Count > 1);
        // Counted exactly as sent: the marker is a real character Twitch counts.
        Assert.All(chunks, c => Assert.True(c.Length <= 500));
        // Every later chunk starts at the start of an entry, and every chunk ends at the end of one.
        Assert.All(chunks.Skip(1), c => Assert.True(c[0] == '#' && char.IsDigit(c[1])));
        Assert.All(
            chunks,
            c =>
            {
                string tail = c.TrimEnd('​');
                Assert.True(tail.EndsWith("min)") || tail.EndsWith("min) again"));
            }
        );
        string reassembled = string.Join(
            " | ",
            chunks.Select((c, i) => i == 0 ? c[BotEmittedLine.Marker.Length..] : c)
        );
        Assert.Equal(body, reassembled);
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
