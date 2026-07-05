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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// Proves fast EventSub recovery on re-auth: a streamer Twitch token (re)vault re-subscribes that channel's topic
/// set immediately (the idempotent <c>EnsureSubscribedAsync</c>), while a non-streamer-Twitch refresh (bot account,
/// a different provider, or a broadcaster-less connection) does NOT drive a per-channel subscribe — so the read
/// feed recovers on reconnect without the ~5-minute reconcile wait, and routine non-channel refreshes stay quiet.
/// </summary>
public sealed class EventSubResubscribeOnTokenRefreshedHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000ab01");

    private static (
        EventSubResubscribeOnTokenRefreshedHandler Handler,
        ITwitchEventSubService EventSub
    ) Build()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        EventSubResubscribeOnTokenRefreshedHandler handler = new(
            eventSub,
            NullLogger<EventSubResubscribeOnTokenRefreshedHandler>.Instance
        );
        return (handler, eventSub);
    }

    private static IntegrationTokenRefreshedEvent Event(string provider, Guid broadcaster) =>
        new()
        {
            BroadcasterId = broadcaster,
            ConnectionId = Guid.CreateVersion7(),
            Provider = provider,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        };

    [Fact]
    public async Task Revaulting_the_streamer_twitch_token_resubscribes_the_channel_immediately()
    {
        (EventSubResubscribeOnTokenRefreshedHandler handler, ITwitchEventSubService eventSub) =
            Build();

        await handler.HandleAsync(Event(AuthEnums.IntegrationProvider.Twitch, Broadcaster));

        // The channel is re-subscribed to its full (non-empty) EventSub topic set on the re-vault — the read feed
        // recovers at once instead of waiting for the 5-minute reconcile.
        await eventSub
            .Received(1)
            .EnsureSubscribedAsync(
                Broadcaster,
                Arg.Is<IReadOnlyCollection<string>>(topics => topics.Count > 0),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_bot_account_refresh_does_not_drive_a_per_channel_resubscribe()
    {
        (EventSubResubscribeOnTokenRefreshedHandler handler, ITwitchEventSubService eventSub) =
            Build();

        await handler.HandleAsync(
            Event(AuthEnums.IntegrationProvider.Twitch + "_bot", Broadcaster)
        );

        await eventSub
            .DidNotReceive()
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_non_twitch_provider_refresh_is_ignored()
    {
        (EventSubResubscribeOnTokenRefreshedHandler handler, ITwitchEventSubService eventSub) =
            Build();

        await handler.HandleAsync(Event("spotify", Broadcaster));

        await eventSub
            .DidNotReceive()
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_broadcasterless_twitch_connection_is_ignored()
    {
        (EventSubResubscribeOnTokenRefreshedHandler handler, ITwitchEventSubService eventSub) =
            Build();

        await handler.HandleAsync(Event(AuthEnums.IntegrationProvider.Twitch, Guid.Empty));

        await eventSub
            .DidNotReceive()
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }
}
