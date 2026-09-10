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

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IOverlayRetractionNotifier"/> abstraction to the
/// <see cref="IWidgetNotifier"/> SignalR hub — bridges the Infrastructure→API dependency boundary so
/// <c>OverlayModerationRetractionHandler</c> never takes a direct reference to the SignalR layer.
/// </summary>
internal sealed class OverlayRetractionNotifierAdapter : IOverlayRetractionNotifier
{
    private readonly IWidgetNotifier _notifier;

    public OverlayRetractionNotifierAdapter(IWidgetNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task RetractAsync(
        Guid broadcasterId,
        string? sourceMessageId,
        string? authorUserId,
        string reason,
        CancellationToken ct = default
    ) =>
        _notifier.RetractAsync(
            broadcasterId.ToString(),
            new(broadcasterId, sourceMessageId, authorUserId, reason),
            ct
        );
}
