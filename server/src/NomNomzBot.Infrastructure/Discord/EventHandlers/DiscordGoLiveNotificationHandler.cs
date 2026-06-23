// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The <c>go_live</c> Discord trigger (discord.md §2). On <see cref="ChannelOnlineEvent"/> it hands off to
/// <see cref="IDiscordNotificationDispatcher"/>, which gates on both-opt-in and dedupes so one stream session
/// posts at most once. A no-op when no enabled <c>go_live</c> rule exists for the channel (the dispatcher
/// returns <c>NOT_FOUND</c>, swallowed here — go-live posting is best-effort and must not affect the live flow).
/// The dedupe key is the stream start instant: one go-live post per session start.
/// </summary>
public sealed class DiscordGoLiveNotificationHandler : IEventHandler<ChannelOnlineEvent>
{
    private const string Trigger = "go_live";

    private readonly IDiscordNotificationDispatcher _dispatcher;
    private readonly ILogger<DiscordGoLiveNotificationHandler> _logger;

    public DiscordGoLiveNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        ILogger<DiscordGoLiveNotificationHandler> logger
    )
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOnlineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        // One dispatch per stream session start (the Stream's Guid is not available on this event — the live
        // Stream id is a ULID string — so the session start instant is the dedupe discriminator).
        string dedupeKey = $"{Trigger}:{@event.StartedAt.UtcDateTime:O}";

        Dictionary<string, string> templateData = new(StringComparer.OrdinalIgnoreCase)
        {
            ["broadcaster"] = @event.BroadcasterDisplayName,
            ["channel.name"] = @event.BroadcasterDisplayName,
            ["channel.title"] = @event.StreamTitle,
            ["channel.game"] = @event.GameName,
            ["title"] = @event.StreamTitle,
            ["game"] = @event.GameName,
        };

        Result<DiscordDispatchOutcomeDto> result = await _dispatcher.DispatchAsync(
            new DiscordDispatchRequest(
                @event.BroadcasterId,
                Trigger,
                dedupeKey,
                StreamId: null,
                templateData
            ),
            cancellationToken
        );

        // NOT_FOUND simply means no go_live rule is configured — that is the expected no-op, not an error.
        if (result.IsFailure && result.ErrorCode != "NOT_FOUND")
            _logger.LogWarning(
                "Discord go-live dispatch failed for {BroadcasterId}: {Error}",
                @event.BroadcasterId,
                result.ErrorMessage
            );
    }
}
