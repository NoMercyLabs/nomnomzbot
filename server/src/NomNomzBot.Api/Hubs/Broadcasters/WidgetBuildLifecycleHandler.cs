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

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Bridges the widget lifecycle to the live overlay. A successful compile (or a rollback) cache-busts the
/// overlay via <c>WidgetReload</c> — this is what makes compile-on-save hot-swap the on-stream widget — and a failed
/// build pushes a compile-failed notice a connected editor can surface. Saved settings go to the open overlay as
/// they are, with no rebuild and no reload. <c>WidgetService</c> publishes these events; this handler is the only
/// thing that turns them into hub pushes.
/// </summary>
public sealed class WidgetBuildLifecycleHandler(IWidgetNotifier notifier)
    : IEventHandler<WidgetBuildSucceededEvent>,
        IEventHandler<WidgetBuildFailedEvent>,
        IEventHandler<WidgetSettingsChangedEvent>
{
    public Task HandleAsync(
        WidgetSettingsChangedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        notifier.SendSettingsChangedAsync(
            @event.BroadcasterId.ToString(),
            @event.WidgetId.ToString(),
            new(@event.WidgetId.ToString(), @event.Settings),
            cancellationToken
        );

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
            new(@event.WidgetId.ToString(), @event.VersionNumber, @event.BuildError),
            cancellationToken
        );
}
