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

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// The plan settings of this deployment, from the <c>Billing</c> section. They decide the tier of a channel with no
/// subscription and no live gift, and the trial a new sign-up gets. A change is a setting and a restart.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// The tier key of a channel with no subscription and no live gift. A key that names no live tier falls back
    /// to <c>base</c>. Set it to <c>hosted_free</c> to put those channels on the Free plan.
    /// </summary>
    public string DefaultTierKey { get; set; } = EffectiveTierRule.BaseTierKey;

    /// <summary>
    /// The days of Base a new sign-up gets as a gift. 0 gives no trial. A sign-up that redeems an invite gets the
    /// invite's gift instead.
    /// </summary>
    public int SignUpTrialDays { get; set; }
}
