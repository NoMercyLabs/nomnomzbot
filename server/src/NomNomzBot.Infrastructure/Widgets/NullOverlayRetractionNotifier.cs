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

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// No-op fallback for <see cref="IOverlayRetractionNotifier"/> in contexts with no live overlay
/// connection (worker processes, background services, tests). The API host replaces it with the
/// SignalR-backed <c>OverlayRetractionNotifierAdapter</c> via a later service registration.
/// </summary>
internal sealed class NullOverlayRetractionNotifier : IOverlayRetractionNotifier
{
    public Task RetractAsync(
        Guid broadcasterId,
        string? sourceMessageId,
        string? authorUserId,
        string reason,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
