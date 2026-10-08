// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Moderates on the SAME platform an inbound message arrived on — the moderation counterpart to
/// <see cref="IInboundOriginChatSender"/>. <c>IChatProvider</c> routes by the channel's single primary
/// platform, which is wrong once a channel has several platform connections live at once: a Kick spam
/// message must be deleted on Kick, not on whichever platform happens to be primary. Automatic safety
/// that knows the inbound provider MUST use this instead of <c>IChatProvider</c>.
///
/// Every method reports what the platform really did (<see cref="InboundModerationOutcome"/>) and never
/// throws for an expected failure. A provider with no registered chat platform is
/// <see cref="InboundModerationStatus.NotSupported"/> — it is NEVER routed to Twitch or any other
/// platform, and no call is attempted anywhere.
/// </summary>
public interface IInboundOriginModerator
{
    /// <summary>Deletes the chat message <paramref name="messageId"/> on <paramref name="provider"/>.</summary>
    Task<InboundModerationOutcome> DeleteMessageAsync(
        Guid broadcasterId,
        string provider,
        string messageId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Times <paramref name="userId"/> out for <paramref name="durationSeconds"/> on <paramref name="provider"/>.</summary>
    Task<InboundModerationOutcome> TimeoutUserAsync(
        Guid broadcasterId,
        string provider,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Bans <paramref name="userId"/> on <paramref name="provider"/>.</summary>
    Task<InboundModerationOutcome> BanUserAsync(
        Guid broadcasterId,
        string provider,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );
}
