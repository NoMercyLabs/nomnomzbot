// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>Audit trail for a feature-flag admin write (platform-conventions §2 / rollout-updates §5).</summary>
public sealed class FeatureFlagAdministeredEvent : DomainEventBase
{
    public required string FlagKey { get; init; }

    /// <summary><c>flag_set</c> | <c>override_set</c> | <c>override_removed</c>.</summary>
    public required string Action { get; init; }
    public Guid? ActorUserId { get; init; }
}
