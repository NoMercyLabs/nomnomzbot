// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using NomNomzBot.Application.Contracts.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the loop-guard marker itself (S009b) is safe to prepend to a real outbound line: it is a
/// Unicode FORMAT character (category <c>Cf</c>), the same class as zero-width joiners/separators that
/// every conformant text renderer (browsers, native chat clients) draws as zero width — so a viewer never
/// sees stray glyphs in a bot-emitted chat line, and stripping it back off recovers the exact text a
/// human typed. This does NOT prove any one platform's API (Twitch Helix, Kick, YouTube Live Chat)
/// preserves the codepoint byte-for-byte on its own round trip — that is asserted per platform by the
/// send→ingest round-trip tests beside each <c>IChatPlatform</c> implementation's tests.
/// </summary>
public sealed class BotEmittedLineTests
{
    [Fact]
    public void Stamp_prepends_the_marker_and_leaves_the_visible_text_untouched()
    {
        const string humanText = "gg well played, that raid was huge!";

        string stamped = BotEmittedLine.Stamp(humanText);

        stamped.Should().Be(BotEmittedLine.Marker + humanText);
        stamped
            .Substring(BotEmittedLine.Marker.Length)
            .Should()
            .Be(humanText, "the marker must not alter or truncate the text a viewer reads");
    }

    [Fact]
    public void The_marker_is_a_unicode_format_character_that_renders_as_zero_width()
    {
        BotEmittedLine.Marker.Should().HaveLength(1, "a single codepoint, not a visible sequence");
        CharUnicodeInfo
            .GetUnicodeCategory(BotEmittedLine.Marker[0])
            .Should()
            .Be(
                UnicodeCategory.Format,
                "a Format-category codepoint (like U+2063 INVISIBLE SEPARATOR) is defined to occupy no "
                    + "rendered width in any conformant text engine — the same class Twitch/Kick/YouTube's "
                    + "own chat clients already rely on for other zero-width formatting codepoints"
            );
    }

    [Fact]
    public void IsMarked_recognizes_the_marker_wherever_it_sits_in_the_line()
    {
        BotEmittedLine.IsMarked(BotEmittedLine.Marker + "leading").Should().BeTrue();
        BotEmittedLine.IsMarked("no marker here").Should().BeFalse();
    }
}
