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
    public bool IsEnabled { get; set; } = EarningRuleDefaults.IsEnabled;
    public long Rate { get; set; } = EarningRuleDefaults.Rate;
    public int? UnitWindowSeconds { get; set; }
    public long? PerWindowCap { get; set; }
    public long? PerStreamCap { get; set; }
    public int? MinRoleLevel { get; set; }
    public int ConfigSchemaVersion { get; set; } = EarningRuleDefaults.ConfigSchemaVersion;
    public string? BonusConfigJson { get; set; }
}
