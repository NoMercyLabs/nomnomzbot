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
