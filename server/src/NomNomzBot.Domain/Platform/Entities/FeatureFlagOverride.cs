// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// A per-tenant override of a <see cref="FeatureFlag"/> (schema P.13). The highest-precedence input to evaluation:
/// an unexpired override forces the flag on/off for that channel regardless of the global toggle/ramp — the basis
/// for internal/beta opt-in and the per-channel kill-switch (rollout-updates §5).
/// </summary>
public class FeatureFlagOverride : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FeatureFlagId { get; set; }
    public Guid BroadcasterId { get; set; }
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
