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
using NomNomzBot.Domain.Economy.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a chat game's lifecycle reaches that channel's dashboards as a <c>games</c> config change.</summary>
public sealed class LiveGameBroadcastHandlerTests
{
    [Fact]
    public async Task A_started_game_pushes_a_created_games_change_naming_the_session()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        Guid session = Guid.CreateVersion7();

        await new LiveGameBroadcastHandler(notifier).HandleAsync(
            new LiveGameStartedEvent
            {
                BroadcasterId = channel,
                SessionId = session,
                GameType = "trivia",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Domain == "games"
                    && dto.EntityId == session.ToString()
                    && dto.Action == "created"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_canceled_game_pushes_an_updated_games_change()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new LiveGameBroadcastHandler(notifier).HandleAsync(
            new LiveGameCancelledEvent
            {
                BroadcasterId = channel,
                SessionId = Guid.CreateVersion7(),
                GameType = "trivia",
                Reason = "host_cancel",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto => dto.Domain == "games" && dto.Action == "updated"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_resolution_pushes_nothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new LiveGameBroadcastHandler(notifier).HandleAsync(
            new LiveGameResolvedEvent
            {
                BroadcasterId = Guid.Empty,
                SessionId = Guid.CreateVersion7(),
                GameType = "trivia",
                ParticipantCount = 2,
                WinnerCount = 1,
                TotalPaidOut = 10,
            }
        );

        await notifier.DidNotReceive().SendConfigChangedAsync(default!, default!, default);
    }
}
