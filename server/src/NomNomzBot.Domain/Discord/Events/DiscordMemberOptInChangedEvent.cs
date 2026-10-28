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
