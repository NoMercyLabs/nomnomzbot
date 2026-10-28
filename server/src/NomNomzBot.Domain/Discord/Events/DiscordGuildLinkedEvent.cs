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
