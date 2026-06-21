// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// One per-channel rule for accruing currency from an engagement <see cref="EarningSource"/> (economy.md K.1a).
/// Opt-in (<c>IsEnabled</c> defaults false); <c>Rate</c> × units, clamped by the per-window / per-stream caps
/// and gated by <c>MinRoleLevel</c>. One rule per <c>(BroadcasterId, Source)</c>.
/// </summary>
public class EarningRule : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public EarningSource Source { get; set; }
    public bool IsEnabled { get; set; }
    public long Rate { get; set; }
    public int? UnitWindowSeconds { get; set; }
    public long? PerWindowCap { get; set; }
    public long? PerStreamCap { get; set; }
    public int? MinRoleLevel { get; set; }
    public int ConfigSchemaVersion { get; set; }
    public string? BonusConfigJson { get; set; }
}
