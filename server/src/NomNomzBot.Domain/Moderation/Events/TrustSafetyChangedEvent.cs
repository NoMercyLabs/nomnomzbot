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

/// <summary>
/// An operator changed the platform trust and safety desk: confirmed or overturned a reviewed automatic
/// action, or applied or lifted a network-wide block. Platform-scoped, so the inherited
/// <c>BroadcasterId</c> stays <c>Guid.Empty</c>. The operator hub pushes it so every open desk reloads.
/// </summary>
public sealed class TrustSafetyChangedEvent : DomainEventBase
{
    /// <summary><c>review</c> | <c>network-block</c>.</summary>
    public required string Area { get; init; }

    /// <summary><c>confirmed</c> | <c>overturned</c> | <c>applied</c> | <c>lifted</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The detection id for a review, the block id for a network block.</summary>
    public required Guid SubjectId { get; init; }
}
