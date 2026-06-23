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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the go-live handler (discord.md §2): a <see cref="ChannelOnlineEvent"/> dispatches the <c>go_live</c>
/// trigger for that channel through <see cref="IDiscordNotificationDispatcher"/> with the stream's data, and is
/// a harmless no-op when nothing is configured (<c>NOT_FOUND</c>) — go-live posting must never disrupt the live
/// flow.
/// </summary>
public sealed class DiscordGoLiveNotificationHandlerTests
{
    [Fact]
    public async Task HandleAsync_DispatchesGoLiveTrigger_ForTheChannel()
    {
        IDiscordNotificationDispatcher dispatcher =
            Substitute.For<IDiscordNotificationDispatcher>();
        dispatcher
            .DispatchAsync(Arg.Any<DiscordDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new DiscordDispatchOutcomeDto(Guid.CreateVersion7(), "sent", "m1", null)
                )
            );

        Guid channel = Guid.CreateVersion7();
        DiscordGoLiveNotificationHandler handler = new(
            dispatcher,
            NullLogger<DiscordGoLiveNotificationHandler>.Instance
        );

        await handler.HandleAsync(
            new ChannelOnlineEvent
            {
                BroadcasterId = channel,
                BroadcasterDisplayName = "Stoney",
                StreamTitle = "blame the lag",
                GameName = "Just Chatting",
                StartedAt = new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero),
            }
        );

        // It dispatched the go_live trigger for THIS channel, with the stream data as template variables.
        await dispatcher
            .Received(1)
            .DispatchAsync(
                Arg.Is<DiscordDispatchRequest>(r =>
                    r.BroadcasterId == channel
                    && r.TriggerType == "go_live"
                    && r.TemplateData["broadcaster"] == "Stoney"
                    && r.TemplateData["title"] == "blame the lag"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_NoConfiguredRule_IsANoOp()
    {
        IDiscordNotificationDispatcher dispatcher =
            Substitute.For<IDiscordNotificationDispatcher>();
        // The dispatcher reports NOT_FOUND when no go_live rule exists — the handler must swallow it silently.
        dispatcher
            .DispatchAsync(Arg.Any<DiscordDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<DiscordDispatchOutcomeDto>(
                    "No enabled Discord rule for this trigger.",
                    "NOT_FOUND"
                )
            );

        DiscordGoLiveNotificationHandler handler = new(
            dispatcher,
            NullLogger<DiscordGoLiveNotificationHandler>.Instance
        );

        // Must not throw — go-live posting is best-effort.
        Func<Task> act = () =>
            handler.HandleAsync(
                new ChannelOnlineEvent
                {
                    BroadcasterId = Guid.CreateVersion7(),
                    BroadcasterDisplayName = "Stoney",
                    StreamTitle = "t",
                    GameName = "g",
                    StartedAt = DateTimeOffset.UtcNow,
                }
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotDispatch()
    {
        IDiscordNotificationDispatcher dispatcher =
            Substitute.For<IDiscordNotificationDispatcher>();
        DiscordGoLiveNotificationHandler handler = new(
            dispatcher,
            NullLogger<DiscordGoLiveNotificationHandler>.Instance
        );

        await handler.HandleAsync(
            new ChannelOnlineEvent
            {
                BroadcasterId = Guid.Empty, // platform sentinel — not a real tenant
                BroadcasterDisplayName = "x",
                StreamTitle = "t",
                GameName = "g",
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        await dispatcher
            .DidNotReceive()
            .DispatchAsync(Arg.Any<DiscordDispatchRequest>(), Arg.Any<CancellationToken>());
    }
}
