// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Engagement.Dtos;
using NomNomzBot.Application.Engagement.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Engagement.EventHandlers;

/// <summary>
/// The chat hot-path hook for engagement detection (engagement.md §6). Provider-agnostic — it rides the
/// canonical <see cref="ChatMessageReceivedEvent"/>, so first-time/returning/streak greetings work on
/// Twitch, YouTube and Kick for free. Cheap by construction: a fully-disabled channel returns after ONE
/// indexed config read, before resolving the live session or the viewer identity. Runs only WHILE LIVE
/// (engagement moments are a live-only feature). Uses its own DB scope to avoid contending with the
/// parallel chat-persistence handler in the dispatch scope.
/// </summary>
public sealed class EngagementChatActivityHandler(
    IServiceScopeFactory scopeFactory,
    IChannelRegistry registry
) : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.UserId))
            return;

        // Bot-side standing (J.12): a muted/shadowbanned chatter triggers no engagement moments.
        if (
            registry
                .Get(@event.BroadcasterId)
                ?.ModerationStandingFor(@event.Provider, @event.UserId)
            is not null
        )
            return;

        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Fast path (default-deny): a channel with no engagement trigger enabled does no further work.
        bool anyEnabled = await db
            .EngagementConfigs.AsNoTracking()
            .Where(c => c.BroadcasterId == @event.BroadcasterId)
            .AnyAsync(
                c => c.FirstTimeChatterEnabled || c.ReturningChatterEnabled || c.WatchStreakEnabled,
                cancellationToken
            );
        if (!anyEnabled)
            return;

        // Live-only: resolve the covering stream session. Offline (no covering stream) → no engagement.
        ILiveWindowResolver liveWindow =
            scope.ServiceProvider.GetRequiredService<ILiveWindowResolver>();
        string? sessionId = await liveWindow.GetCoveringStreamIdAsync(
            @event.BroadcasterId,
            @event.OccurredAt.UtcDateTime,
            cancellationToken
        );
        if (string.IsNullOrEmpty(sessionId))
            return;

        // Resolve the viewer's internal User Guid (identity-keyed by the message's Provider).
        IUserService users = scope.ServiceProvider.GetRequiredService<IUserService>();
        Result<UserDto> user = await users.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            cancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
            return;

        IEngagementService engagement =
            scope.ServiceProvider.GetRequiredService<IEngagementService>();
        await engagement.OnChatActivityAsync(
            @event.BroadcasterId,
            new EngagementSignal(
                viewerUserId,
                @event.UserId,
                @event.UserDisplayName,
                sessionId,
                @event.OccurredAt.UtcDateTime
            ),
            cancellationToken
        );
    }
}
