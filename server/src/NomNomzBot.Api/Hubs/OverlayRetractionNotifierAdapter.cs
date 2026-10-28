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
