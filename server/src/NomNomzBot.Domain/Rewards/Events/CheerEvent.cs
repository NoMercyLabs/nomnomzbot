// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a viewer cheers with bits (Twitch EventSub channel.cheer) or the platform-equivalent
/// paid on-platform currency — e.g. Kick's Kicks (webhook kicks.gifted, <see cref="Bits"/> = amount);
/// <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class CheerEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this cheer was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required int Bits { get; init; }
    public required string Message { get; init; }
    public required bool IsAnonymous { get; init; }
}
