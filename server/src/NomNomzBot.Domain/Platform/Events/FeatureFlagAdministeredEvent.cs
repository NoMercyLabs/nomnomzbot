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

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>Audit trail for a feature-flag admin write (platform-conventions §2 / rollout-updates §5).</summary>
public sealed class FeatureFlagAdministeredEvent : DomainEventBase
{
    /// <summary>The key of the feature flag that was changed.</summary>
    public required string FlagKey { get; init; }

    /// <summary><c>flag_set</c> | <c>override_set</c> | <c>override_removed</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The id of the admin who made the change. Empty when the system made it.</summary>
    public Guid? ActorUserId { get; init; }
}
