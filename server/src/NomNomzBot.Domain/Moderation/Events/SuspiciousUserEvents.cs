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
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string LowTrustStatus { get; init; }
    public required string MessageId { get; init; }
    public required string Text { get; init; }
    public required string BanEvasionEvaluation { get; init; }
}

/// <summary>
/// A moderator changed a chatter's suspicious-user treatment (<c>channel.suspicious_user.update</c>).
/// <see cref="LowTrustStatus"/> is the new flag — <c>none</c> (cleared), <c>active_monitoring</c>, or
/// <c>restricted</c>.
/// </summary>
public sealed class SuspiciousUserUpdatedEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required string LowTrustStatus { get; init; }
}
