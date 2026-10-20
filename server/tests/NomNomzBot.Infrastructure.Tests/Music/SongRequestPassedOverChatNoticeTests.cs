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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>A request that waits for the Music Player page is announced in chat, naming the song.</summary>
public sealed class SongRequestPassedOverChatNoticeTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000a7d02");

    [Fact]
    public async Task ChatNamesTheSongThatWaitsForThePlayerPage()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        List<string> sent = [];
        chat.SendMessageAsync(Channel, Arg.Do<string>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(true);

        await new SongRequestPassedOverChatNotice(chat).HandleAsync(
            new SongRequestPassedOverEvent
            {
                BroadcasterId = Channel,
                TrackUri = "https://www.youtube.com/watch?v=abc",
                TrackName = "Movin' To The Sun",
            }
        );

        sent.Should()
            .Equal(
                "\"Movin' To The Sun\" is waiting for the Music Player page to open, so the next requests play first."
            );
    }
}
