// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Ingested custom-data payload → the <c>custom_data</c> overlay widget (custom-events.md §3). Routes each
/// <see cref="CustomDataReceivedEvent"/> as a widget event whose type is the source-derived
/// <c>custom.&lt;source name&gt;</c> — the exact string a widget binds in its EventSubscriptions (e.g.
/// <c>custom.heartrate</c>) — carrying <c>{ fields: { name: value } }</c>, the extracted-fields map the
/// widget reads its configured field from. Subscription-matched, so a source's data only reaches widgets
/// bound to that source.
/// </summary>
public sealed class CustomDataBroadcastHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<CustomDataReceivedEvent>
{
    public Task HandleAsync(
        CustomDataReceivedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            $"custom.{@event.SourceName}",
            new { fields = @event.Fields },
            cancellationToken
        );
}
