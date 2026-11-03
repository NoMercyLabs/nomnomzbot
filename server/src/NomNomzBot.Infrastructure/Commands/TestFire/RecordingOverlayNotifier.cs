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

using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Infrastructure.Commands.TestFire;

/// <summary>Pushes every alert to the real overlay notifier and remembers its resolved message.</summary>
internal sealed class RecordingOverlayNotifier(IEventResponseOverlayNotifier inner)
    : IEventResponseOverlayNotifier
{
    private readonly List<string> _alerts = [];

    /// <summary>The resolved alert messages that were sent, in order.</summary>
    public IReadOnlyList<string> Alerts => _alerts;

    public async Task NotifyAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string resolvedMessage,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default
    )
    {
        await inner.NotifyAsync(broadcasterId, eventTypeKey, resolvedMessage, metadata, ct);
        _alerts.Add(resolvedMessage);
    }
}
