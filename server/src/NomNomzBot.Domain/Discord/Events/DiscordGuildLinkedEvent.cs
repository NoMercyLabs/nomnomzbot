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
    public required Guid GuildConnectionId { get; init; }
    public required string GuildId { get; init; }
    public required string GuildName { get; init; }
}
