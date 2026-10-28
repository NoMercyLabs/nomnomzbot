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
