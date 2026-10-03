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

namespace NomNomzBot.Domain.Discord.Events;

/// <summary>
/// Published when a member self-assigns/removes a notify role (command/button/role sync). Drives opt-in count
/// refresh. The publisher sets the inherited <c>BroadcasterId</c> to the role's channel; tenant-scoped, never
/// <c>Guid.Empty</c>.
/// </summary>
public sealed class DiscordMemberOptInChangedEvent : DomainEventBase
{
    /// <summary>The internal id of the notification role the member opted in to or out of.</summary>
    public required Guid NotificationRoleId { get; init; }

    /// <summary>The Discord user id of the member (a number as text).</summary>
    public required string DiscordMemberId { get; init; }

    /// <summary>True when the member opted in to the notification role. False when the member opted out.</summary>
    public required bool OptedIn { get; init; }

    /// <summary><c>manual_role</c> | <c>command</c> | <c>button</c>.</summary>
    public required string Source { get; init; }
}
