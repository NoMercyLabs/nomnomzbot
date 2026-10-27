// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Emotes;

public sealed class EmoteHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // One emote by name: exact, case-insensitive fallback, missing, with a fallback text.
            { "{emote:catJAM}", "catJAM" },
            { "{emote:catjam}", "catJAM" },
            { "{emote:Nope}", "" },
            { "{emote:Nope|:)}", ":)" },
            { "{emote:catJAM|:)}", "catJAM" },
            { "{emote.repeat:catJAM|3}", "catJAM catJAM catJAM" },
            { "{emote.repeat:Nope|3}", "" },
            { "{emote.wall:catJAM|2|2}", "catJAM catJAM / catJAM catJAM" },
            { "{emote.wall:KEKW|1|3}", "KEKW KEKW KEKW" },
            { "{emote.image:catJAM}", "https://cdn.7tv.app/catJAM/2x.webp" },
            {
                "{emote.image:Kappa}",
                "https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/2.0"
            },
            { "{emote.provider:catJAM}", "7TV" },
            { "{emote.provider:widepeepo}", "BTTV" },
            { "{emote.provider:monkaS}", "FFZ" },
            { "{emote.provider:Kappa}", "Twitch" },
            { "{emote.provider:sampleHype}", "Twitch" },
            // Lists and counts per set.
            { "{emote.list.channel}", "sampleHype sampleLove sampleWave sampleBits" },
            { "{emote.list.7tv}", "catJAM KEKW PepeLaugh" },
            { "{emote.list.bttv}", "peepoHappy widepeepo" },
            { "{emote.list.ffz}", "monkaS" },
            { "{emote.count.channel}", "4" },
            { "{emote.count.7tv}", "3" },
            { "{emote.count.bttv}", "2" },
            { "{emote.count.ffz}", "1" },
            // Chat usage (ViewerOne: KEKW 4, catJAM 1; the channel: KEKW 9; this stream: catJAM 5).
            { "{emote.channel.top}", "KEKW" },
            { "{emote.channel.top.stream}", "catJAM" },
            { "{emote.user.top}", "KEKW" },
            { "{emote.user.top.count}", "4" },
            // The trigger message holds KEKW, catJAM and KEKW.
            { "{emote.message.count}", "3" },
            { "{emote.message.first}", "KEKW" },
            // The owner's newest favorite.
            { "{emote.user.favorite}", "catJAM" },
            // The biggest cheermote tier that fits; a tier that cannot be cheered is skipped.
            { "{emote.cheermote:100}", "Cheer100" },
            { "{emote.cheermote:150}", "Cheer100" },
            { "{emote.cheermote:1}", "Cheer1" },
            { "{emote.cheermote:1000}", "Cheer1000" },
            { "{emote.cheermote:6000}", "Cheer1000" },
            // A nested token feeds an argument.
            { "{emote.repeat:{emote.message.first}|2}", "KEKW KEKW" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_fixture(
        string template,
        string expected
    )
    {
        await using EmoteHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}
