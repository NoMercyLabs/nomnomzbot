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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ChannelUpdatedBroadcastHandler"/> forwards a title/category change to dashboard clients with
/// the new title/game mapped onto <see cref="StreamInfoChangedDto"/> — the gap this closes: the read-model
/// handler persisted the change but no hub client ever heard about it.
/// </summary>
public sealed class ChannelUpdatedBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_MapsNewTitleAndGame_OntoStreamInfoChangedDto()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ChannelUpdatedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                BroadcasterDisplayName = "Stoney",
                NewTitle = "blame the lag",
                NewGameName = "Just Chatting",
            }
        );

        await notifier
            .Received(1)
            .SendStreamInfoChangedAsync(
                channel.ToString(),
                Arg.Is<StreamInfoChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.BroadcasterDisplayName == "Stoney"
                    && dto.Title == "blame the lag"
                    && dto.GameName == "Just Chatting"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ChannelUpdatedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                BroadcasterDisplayName = "x",
                NewTitle = "t",
                NewGameName = "g",
            }
        );

        await notifier
            .DidNotReceive()
            .SendStreamInfoChangedAsync(
                Arg.Any<string>(),
                Arg.Any<StreamInfoChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
