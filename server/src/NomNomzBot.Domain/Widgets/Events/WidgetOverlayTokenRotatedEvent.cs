// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
