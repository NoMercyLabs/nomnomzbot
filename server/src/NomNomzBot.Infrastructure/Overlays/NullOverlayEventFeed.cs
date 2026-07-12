// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
