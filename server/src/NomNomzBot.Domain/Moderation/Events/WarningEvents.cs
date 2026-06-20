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

/// <summary>A warned user acknowledged their warning, restoring their ability to chat (<c>channel.warning.acknowledge</c>).</summary>
public sealed class WarningAcknowledgedEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
}

/// <summary>
/// A moderator sent a warning to a user (<c>channel.warning.send</c>). <see cref="Reason"/> is the moderator's
/// free-text reason (null when omitted) and <see cref="ChatRulesCited"/> is the set of channel chat rules cited
/// in the warning (empty when none were attached).
/// </summary>
public sealed class WarningSentEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<string> ChatRulesCited { get; init; }
}
