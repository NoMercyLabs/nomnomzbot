// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// No-op fallback for <see cref="IWidgetEventNotifier"/> in contexts with no live overlay connection
/// (worker processes, background services, tests). The API host replaces it with the SignalR-backed
/// <c>WidgetEventNotifierAdapter</c> via a later service registration.
/// </summary>
internal sealed class NullWidgetEventNotifier : IWidgetEventNotifier
{
    public Task SendWidgetEventAsync(
        Guid broadcasterId,
        Guid widgetId,
        string eventType,
        object? data,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
