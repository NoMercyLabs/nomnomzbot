// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// SOURCE-TEXT guard for the now-playing title marquee. The cycle is: fade in, slowly move the text to the
/// end of the line, fade out, reset while invisible, replay. The browser animation cannot run here, so this
/// pins the keyframe order and the measuring rules.
/// </summary>
public sealed class NowPlayingMarqueeGuardTests
{
    private static string Source()
    {
        const string resourceName =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.now_playing.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the now-playing widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static string Keyframes()
    {
        Match match = Regex.Match(
            Source(),
            @"@keyframes nnz-marquee \{(?<body>.*?)\n\}",
            RegexOptions.Singleline
        );
        match.Success.Should().BeTrue("the marquee keyframes must exist");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void Marquee_fades_in_then_moves_then_fades_out_then_resets_hidden()
    {
        string frames = Keyframes();

        Regex
            .IsMatch(frames, @"0%\s*\{[^}]*translateX\(0\)[^}]*opacity:\s*0")
            .Should()
            .BeTrue("it starts invisible at the start");
        frames.Should().Contain("opacity: 1", "the text fades in");
        Regex
            .IsMatch(frames, @"100%\s*\{[^}]*translateX\(var\(--marquee-distance[^}]*opacity:\s*0")
            .Should()
            .BeTrue("it ends faded out at the far end; the reset to 0 happens while invisible");
    }

    [Fact]
    public void Marquee_moves_linearly_and_is_sized_to_the_overflow()
    {
        string source = Source();

        source
            .Should()
            .Contain("--marquee-duration", "scroll time scales with the overflow distance");
        source.Should().Contain("linear", "a slow constant-speed scroll, not ease-in-out");
        source
            .Should()
            .Contain("textWidth - containerWidth", "distance is the overflow width only");
    }
}
