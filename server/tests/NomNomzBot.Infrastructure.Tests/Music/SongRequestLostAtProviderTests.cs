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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The chat notice for a song request that vanished from the provider's own queue. The detection side
/// (a recovery tick that judged an in-flight request lost after a grace period) is gone: the playback
/// director starts the head directly, so nothing waits in a provider queue to be lost. What stays is the
/// notice handler for <c>SongRequestLostAtProviderEvent</c>.
/// </summary>
public sealed class SongRequestLostAtProviderTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");

    [Fact]
    public async Task The_chat_notice_names_the_requester_and_the_song()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        List<string> sent = [];
        chat.SendMessageAsync(ChannelId, Arg.Do<string>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(true);

        await new SongRequestLostAtProviderChatNotice(chat).HandleAsync(
            new()
            {
                BroadcasterId = ChannelId,
                TrackUri = "spotify:track:family",
                TrackName = "family ties",
                RequestedBy = "anda_six",
            }
        );

        sent.Should().ContainSingle();
        sent[0].Should().StartWith("@anda_six ").And.Contain("\"family ties\"");
    }
}
