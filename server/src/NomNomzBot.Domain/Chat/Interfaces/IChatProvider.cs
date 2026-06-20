// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.Interfaces;

/// <summary>
/// Abstraction for sending chat messages and performing moderation actions.
/// <c>broadcasterId</c> is the tenant (channel) <see cref="Guid"/>; the implementation resolves it to the
/// Twitch channel string id before any Helix/IRC call (the invariant: Twitch never receives a Guid).
/// <c>userId</c> targets are Twitch user string ids (they arrive from Twitch events / template vars).
/// </summary>
public interface IChatProvider
{
    Task SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );

    Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    );
}
