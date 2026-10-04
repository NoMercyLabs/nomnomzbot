// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// A rotation can retire a token at once (a second rotation drops the oldest one), so the widget's open pages
/// are checked the moment the rotation happens, not only on the next timed sweep.
/// </summary>
public sealed class OverlayTokenRotatedHandler(OverlayTokenSweeper sweeper)
    : IEventHandler<WidgetOverlayTokenRotatedEvent>
{
    public async Task HandleAsync(
        WidgetOverlayTokenRotatedEvent @event,
        CancellationToken cancellationToken = default
    ) => await sweeper.SweepAsync(@event.WidgetId, cancellationToken);
}
