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
    /// <summary>
    /// Whether a chat platform is registered for <paramref name="provider"/>, i.e. whether the actions below
    /// can be attempted there at all. The one answer shared by enforcement and the protection status, so what
    /// the dashboard says automatic moderation can reach is what enforcement really tries.
    /// </summary>
    bool Supports(string provider);

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
