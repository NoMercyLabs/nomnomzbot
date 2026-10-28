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

using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Infrastructure.Overlays;

/// <summary>
/// The default <see cref="IOverlayEventFeed"/> for hosts without a SignalR overlay hub (background/worker contexts).
/// A no-op so the post-commit fan-out hook resolves and runs harmlessly; the API layer replaces this with the real
/// hub-backed adapter.
/// </summary>
public sealed class NullOverlayEventFeed : IOverlayEventFeed
{
    public Task BroadcastEventAsync(
        Guid broadcasterId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
