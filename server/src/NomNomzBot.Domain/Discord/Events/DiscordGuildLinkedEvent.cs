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
/// Published when a Discord guild reaches both-opt-in (server approved AND streamer enabled). Triggers
/// notification-role / button provisioning. The publisher sets the inherited <c>BroadcasterId</c> to the
/// linked channel; tenant-scoped, never <c>Guid.Empty</c>.
/// </summary>
public sealed class DiscordGuildLinkedEvent : DomainEventBase
{
    /// <summary>The internal id of the link between this channel and the Discord server.</summary>
    public required Guid GuildConnectionId { get; init; }

    /// <summary>The Discord server id (a number as text).</summary>
    public required string GuildId { get; init; }

    /// <summary>The name of the Discord server as shown in Discord. Empty when Discord gave no name.</summary>
    public required string GuildName { get; init; }
}
