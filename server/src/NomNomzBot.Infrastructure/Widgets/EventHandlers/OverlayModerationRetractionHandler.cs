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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>
/// Moderation retraction — deleted content leaves the screen (widgets-overlays.md §2a). A message
/// deleted in chat, or its author timed out/banned/network-nuked, is still shown on any overlay that
/// already rendered it: the deletion never reached OBS. This handler subscribes to the moderation
/// facts the bus already carries, publishes <see cref="OverlayContentRetractedEvent"/> for internal
/// consumers, and pushes <see cref="IOverlayRetractionNotifier"/> so every overlay drops the content
/// the SAME instant the dashboard does. Retracting content with no local correlation (e.g. an author
/// the bot has never seen locally) is a no-op — the push still carries the platform-native id a widget
/// can match against, it never throws.
/// </summary>
public sealed class OverlayModerationRetractionHandler
    : IEventHandler<ChatMessageDeletedEvent>,
        IEventHandler<UserBannedEvent>,
        IEventHandler<UserTimedOutEvent>,
        IEventHandler<NetworkNukeExecutedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly IOverlayRetractionNotifier _overlay;
    private readonly IEventBus _eventBus;

    public OverlayModerationRetractionHandler(
        IApplicationDbContext db,
        IOverlayRetractionNotifier overlay,
        IEventBus eventBus
    )
    {
        _db = db;
        _overlay = overlay;
        _eventBus = eventBus;
    }

    public async Task HandleAsync(
        ChatMessageDeletedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        Guid? retractedBy = await ResolveUserIdAsync(@event.DeletedByUserId, cancellationToken);
        await RetractAsync(
            @event.BroadcasterId,
            @event.MessageId,
            @event.TargetUserId,
            "message_deleted",
            retractedBy,
            cancellationToken
        );
    }

    public async Task HandleAsync(
        UserBannedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        Guid? retractedBy = await ResolveUserIdAsync(@event.ModeratorUserId, cancellationToken);
        await RetractAsync(
            @event.BroadcasterId,
            sourceMessageId: null,
            @event.TargetUserId,
            "user_ban",
            retractedBy,
            cancellationToken
        );
    }

    public async Task HandleAsync(
        UserTimedOutEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        Guid? retractedBy = await ResolveUserIdAsync(@event.ModeratorUserId, cancellationToken);
        await RetractAsync(
            @event.BroadcasterId,
            sourceMessageId: null,
            @event.TargetUserId,
            "user_timeout",
            retractedBy,
            cancellationToken
        );
    }

    public async Task HandleAsync(
        NetworkNukeExecutedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.OriginBroadcasterId == Guid.Empty)
            return;

        // The batch event carries only the origin channel + a leg count, not the per-channel leg list
        // (NetworkNukeService.NukeAsync), so only the origin channel's overlay can be retracted from this
        // event alone — the other legs' channels are out of reach here.
        await RetractAsync(
            @event.OriginBroadcasterId,
            sourceMessageId: null,
            @event.TargetTwitchUserId,
            "mod_retract",
            @event.InitiatedByUserId,
            cancellationToken
        );
    }

    private async Task RetractAsync(
        Guid broadcasterId,
        string? sourceMessageId,
        string? authorPlatformUserId,
        string reason,
        Guid? retractedByUserId,
        CancellationToken ct
    )
    {
        Guid? authorUserId = string.IsNullOrEmpty(authorPlatformUserId)
            ? null
            : await ResolveUserIdAsync(authorPlatformUserId, ct);

        await _eventBus.PublishAsync(
            new OverlayContentRetractedEvent
            {
                BroadcasterId = broadcasterId,
                SourceMessageId = sourceMessageId,
                AuthorUserId = authorUserId,
                Reason = reason,
                RetractedByUserId = retractedByUserId,
            },
            ct
        );

        // The wire push carries the PLATFORM-native author id, not the resolved local one — a widget
        // correlates against whatever it already attached to its rendered content, which is always the
        // platform id (ChatMessageReceivedEvent.UserId), never an internal Guid it never saw.
        await _overlay.RetractAsync(
            broadcasterId,
            sourceMessageId,
            authorPlatformUserId,
            reason,
            ct
        );
    }

    private async Task<Guid?> ResolveUserIdAsync(string platformUserId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(platformUserId))
            return null;

        Guid id = await _db
            .Users.Where(u => u.TwitchUserId == platformUserId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        return id == Guid.Empty ? null : id;
    }
}
