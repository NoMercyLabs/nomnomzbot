// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Billing;

/// <summary>
/// Classifies a limited resource by whether it maps to a real marginal cost (S-BUDGETS-a). The owner's binding
/// intent: limits exist to recover real cost, never to manufacture upsell pressure.
/// </summary>
public enum ResourceClass
{
    /// <summary>
    /// Maps to a real bill: stored bytes, TTS characters, script CPU, external API volume, bandwidth, retained
    /// history rows. Tier-scaled; self-host resolves to unlimited (the operator pays their own hosting).
    /// </summary>
    CostDriving,

    /// <summary>
    /// A DB row that costs effectively nothing to serve (a command, a timer, a pipeline, an event response).
    /// Capped ONLY against abuse at one generous safety baseline applied uniformly to every tenant, self-host
    /// included — never tier-scaled, never a paid ceiling.
    /// </summary>
    NearFree,
}
