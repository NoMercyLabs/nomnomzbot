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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>
/// A widget's overlay token was rotated. Carries no token: a listener re-reads which tokens are still valid
/// from the widget row, so no secret travels on the event bus. Open pages on a token that is no longer valid
/// must close.
/// </summary>
public sealed class WidgetOverlayTokenRotatedEvent : DomainEventBase
{
    /// <summary>The id of the widget whose token was rotated.</summary>
    public required Guid WidgetId { get; init; }
}
