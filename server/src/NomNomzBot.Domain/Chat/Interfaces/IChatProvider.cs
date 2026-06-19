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
/// </summary>
public interface IChatProvider
{
    Task SendMessageAsync(
        string broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task SendReplyAsync(
        string broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task TimeoutUserAsync(
        string broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    Task BanUserAsync(
        string broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    Task UnbanUserAsync(
        string broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );

    Task DeleteMessageAsync(
        string broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    );
}
