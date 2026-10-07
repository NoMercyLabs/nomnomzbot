// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The wire shape of the <c>count</c> widget event: the authoritative absolute total of one metric
/// (<c>followers</c> or <c>subs</c>), sent as a seed after an overlay reload. It is not a goal, so it never moves a
/// goal bar; <c>labels.vue</c> sets its running count to <see cref="Value"/>.
/// </summary>
public sealed record CountWidgetEventPayload(string Metric, int Value);
