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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>When a viewer is unbanned from the channel.</summary>
public sealed class UserUnbannedEvent : DomainEventBase
{
    /// <summary>The id of the unbanned viewer (a number as text on Twitch).</summary>
    public required string TargetUserId { get; init; }

    /// <summary>The unbanned viewer's display name (<c>user_name</c>); null on non-Twitch ingests.</summary>
    public string? TargetDisplayName { get; init; }

    /// <summary>The unbanned viewer's login (lowercase, no spaces), or null when the source gave none.</summary>
    public string? TargetLogin { get; init; }

    /// <summary>The id of the moderator who lifted the ban (a number as text on Twitch).</summary>
    public required string ModeratorUserId { get; init; }

    /// <summary>The acting moderator's display name (<c>moderator_user_name</c>); null on non-Twitch ingests.</summary>
    public string? ModeratorDisplayName { get; init; }
}
