// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The unified moderation review queue (moderation.md J.1). This slice covers the AutoMod held-message path:
/// <c>automod.message.hold</c> enqueues a pending row (via <see cref="EnqueueHeldMessageAsync"/>, called by the
/// AutoMod event handler, not the dashboard); a moderator lists the pending queue and resolves each item, which
/// relays through Helix to release/drop the held Twitch message.
/// </summary>
public interface IModerationQueueService
{
    /// <summary>Enqueue a held AutoMod message (source=automod, status=pending). Called by the AutoMod event handler.</summary>
    Task<Result<Guid>> EnqueueHeldMessageAsync(
        Guid broadcasterId,
        string autoModMessageId,
        string twitchUserId,
        string username,
        string messageContent,
        string category,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Mark a held-message row resolved from a Twitch-reported update (another moderator, or Twitch auto-expiry) —
    /// found by its AutoMod message id. A no-op (not a failure) when no matching pending row exists — the update
    /// may race the enqueue, or arrive for a message this instance never held. <c>ResolvedByUserId</c> stays
    /// null: the resolution happened outside this dashboard.
    /// </summary>
    Task ApplyExternalResolutionAsync(
        Guid broadcasterId,
        string autoModMessageId,
        string twitchStatus,
        CancellationToken cancellationToken = default
    );

    /// <summary>List the channel's queue items filtered by <paramref name="status"/> (pending / approved / denied / expired), newest first.</summary>
    Task<Result<List<ModerationQueueItemDto>>> ListAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolve a pending item — <c>approve</c> releases the held message to chat, <c>deny</c> drops it. Relays
    /// through Helix <c>POST /moderation/automod/message</c> before recording the local resolution; a Helix
    /// failure leaves the row pending so the moderator can retry.
    /// </summary>
    Task<Result<ModerationQueueItemDto>> ResolveAsync(
        string broadcasterId,
        Guid queueItemId,
        string action,
        string? resolverUserId,
        CancellationToken cancellationToken = default
    );
}
