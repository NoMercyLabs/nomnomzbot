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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Discord.Events;

/// <summary>
/// Published when a guild link is revoked by a server admin OR disabled by the streamer (no longer both-opt-in)
/// OR fully disconnected. Consumers stop dispatching to it. The publisher sets the inherited
/// <c>BroadcasterId</c> to the unlinked channel; tenant-scoped, never <c>Guid.Empty</c>.
/// </summary>
public sealed class DiscordGuildUnlinkedEvent : DomainEventBase
{
    /// <summary>The internal id of the link between this channel and the Discord server that was removed.</summary>
    public required Guid GuildConnectionId { get; init; }

    /// <summary>The Discord server id (a number as text).</summary>
    public required string GuildId { get; init; }

    /// <summary><c>server_revoked</c> | <c>streamer_disabled</c> | <c>disconnected</c>.</summary>
    public required string Reason { get; init; }
}
