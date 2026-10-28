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

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The wire shape of the <c>count</c> widget event: the authoritative absolute total of one metric
/// (<c>followers</c> or <c>subs</c>), sent as a seed after an overlay reload. It is not a goal, so it never moves a
/// goal bar; <c>labels.vue</c> sets its running count to <see cref="Value"/>.
/// </summary>
public sealed record CountWidgetEventPayload(string Metric, int Value);
