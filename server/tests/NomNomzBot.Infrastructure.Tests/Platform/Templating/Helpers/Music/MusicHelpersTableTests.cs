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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Music;

public sealed class MusicHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Now playing (Song A by Artist A, 1:23 of 3:30, requested by ViewerTwo).
            { "{music.now}", "Song A by Artist A" },
            { "{music.now.title}", "Song A" },
            { "{music.now.artist}", "Artist A" },
            { "{music.now.album}", "Album A" },
            { "{music.now.cover}", "https://i.scdn.co/image/sample" },
            { "{music.now.link}", "https://open.spotify.com/track/sample" },
            { "{music.now.requester}", "ViewerTwo" },
            { "{music.now.requester.mention}", "@ViewerTwo" },
            // Progress and bar.
            { "{music.now.duration}", "3:30" },
            { "{music.now.progress}", "1:23" },
            { "{music.now.remaining}", "2:07" },
            { "{music.now.percent}", "40%" },
            { "{music.now.bar}", "▬▬▬▬🔘▬▬▬▬▬▬ 1:23 / 3:30" },
            { "{music.now.bar:5}", "▬▬🔘▬▬" },
            { "{if:{music.now.isPlaying}|▶️|⏸️}", "▶️" },
            { "{music.now.status}", "playing" },
            { "{music.volume}", "60%" },
            { "{music.shuffle}", "off" },
            { "{music.repeat}", "off" },
            { "{music.provider}", "Spotify" },
            // Queue (five songs, 17m 20s, four people, 1,500 points).
            { "{music.queue.length}", "5" },
            { "{music.queue.duration}", "17m 20s" },
            { "{music.queue.next}", "Song B by Artist B" },
            { "{music.queue.next.requester}", "ViewerThree" },
            { "{music.queue.item:2}", "Song C by Artist C" },
            { "{music.queue.list:3}", "1. Song B, 2. Song C, 3. Song D" },
            { "{music.queue.requesters}", "4" },
            { "{if:{music.queue.isEmpty}|queue is empty|{music.queue.length} songs}", "5 songs" },
            { "{music.queue.cost.total}", "1,500" },
            // ViewerOne at place 3 with two songs; ViewerTwo (the target) has one.
            { "{music.queue.position}", "3" },
            { "{music.queue.position.ordinal}", "3rd" },
            { "{music.queue.wait}", "7m 40s" },
            { "{music.queue.wait.at}", "about 15:38" },
            { "{music.queue.mine.count}", "2" },
            { "{music.queue.mine.titles}", "Song C, Song E" },
            { "{music.queue.mine.code}", "K7QM" },
            { "{music.queue.mine.next}", "Song C" },
            { "{music.queue.target.count}", "1" },
            // History: 21 requests since 12:15Z, one older that must not count.
            { "{music.history.last}", "Song Z by Artist Z" },
            { "{music.history.list:3}", "Song Z, Song Y, Song X" },
            { "{music.history.count.stream}", "21" },
            { "{music.history.topRequester.stream}", "ViewerTwo (4)" },
            { "{music.history.topArtist.stream}", "Artist A" },
            { "{music.user.requests.total}", "9" },
            { "{music.user.requests.stream}", "2" },
            { "{music.user.lastRequest}", "Song C" },
            // The channel's request settings.
            {
                "{if:{music.requests.open}|requests are open|requests are closed}",
                "requests are open"
            },
            { "{music.requests.maxPerUser}", "3" },
            { "{music.requests.maxDuration}", "6:00" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using MusicHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}
