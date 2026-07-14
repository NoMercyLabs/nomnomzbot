// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Bridges the widget build lifecycle to the live overlay. A successful compile (or a rollback) cache-busts the
/// overlay via <c>WidgetReload</c> — this is what makes compile-on-save hot-swap the on-stream widget — and a failed
/// build pushes a compile-failed notice a connected editor can surface. <c>WidgetService</c> publishes the build
/// events; this handler is the only thing that turns them into hub pushes.
/// </summary>
public sealed class WidgetBuildLifecycleHandler(IWidgetNotifier notifier)
    : IEventHandler<WidgetBuildSucceededEvent>,
        IEventHandler<WidgetBuildFailedEvent>
{
    public Task HandleAsync(
        WidgetBuildSucceededEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        notifier.ReloadWidgetAsync(
            @event.BroadcasterId.ToString(),
            @event.WidgetId.ToString(),
            cancellationToken
        );

    public Task HandleAsync(
        WidgetBuildFailedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        notifier.SendCompileFailedAsync(
            @event.BroadcasterId.ToString(),
            @event.WidgetId.ToString(),
            new WidgetCompileFailedDto(
                @event.WidgetId.ToString(),
                @event.VersionNumber,
                @event.BuildError
            ),
            cancellationToken
        );
}
