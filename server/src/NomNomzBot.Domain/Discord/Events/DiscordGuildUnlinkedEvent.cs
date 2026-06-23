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
/// Published when a guild link is revoked by a server admin OR disabled by the streamer (no longer both-opt-in)
/// OR fully disconnected. Consumers stop dispatching to it. The publisher sets the inherited
/// <c>BroadcasterId</c> to the unlinked channel; tenant-scoped, never <c>Guid.Empty</c>.
/// </summary>
public sealed class DiscordGuildUnlinkedEvent : DomainEventBase
{
    public required Guid GuildConnectionId { get; init; }
    public required string GuildId { get; init; }

    /// <summary><c>server_revoked</c> | <c>streamer_disabled</c> | <c>disconnected</c>.</summary>
    public required string Reason { get; init; }
}
