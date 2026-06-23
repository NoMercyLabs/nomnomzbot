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
    public required Guid NotificationRoleId { get; init; }
    public required string DiscordMemberId { get; init; }
    public required bool OptedIn { get; init; }

    /// <summary><c>manual_role</c> | <c>command</c> | <c>button</c>.</summary>
    public required string Source { get; init; }
}
