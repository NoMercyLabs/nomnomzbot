// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A chat message arrived from a flagged suspicious user (<c>channel.suspicious_user.message</c>).
/// <see cref="LowTrustStatus"/> is the chatter's flag — <c>active_monitoring</c> or <c>restricted</c> — and
/// <see cref="BanEvasionEvaluation"/> is Twitch's likelihood the chatter is evading a ban
/// (<c>likely</c>, <c>possible</c>, or <c>unlikely</c>). <see cref="MessageId"/> and <see cref="Text"/> carry the
/// offending message so a moderation pipeline can act on it.
/// </summary>
public sealed class SuspiciousUserMessageEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the flagged chatter (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The chatter's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The chatter's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>How Twitch flags the chatter: active_monitoring or restricted.</summary>
    public required string LowTrustStatus { get; init; }

    /// <summary>The Twitch id of the chat message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The text of the chat message.</summary>
    public required string Text { get; init; }

    /// <summary>How likely Twitch thinks the chatter is dodging a ban: likely, possible or unlikely.</summary>
    public required string BanEvasionEvaluation { get; init; }
}

/// <summary>
/// A moderator changed a chatter's suspicious-user treatment (<c>channel.suspicious_user.update</c>).
/// <see cref="LowTrustStatus"/> is the new flag — <c>none</c> (cleared), <c>active_monitoring</c>, or
/// <c>restricted</c>.
/// </summary>
public sealed class SuspiciousUserUpdatedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the chatter (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The chatter's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The chatter's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The Twitch user id of the moderator who made the change (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The moderator's display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>The new flag: none, active_monitoring or restricted.</summary>
    public required string LowTrustStatus { get; init; }
}
