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

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// No-op fallback for <see cref="IEventResponseOverlayNotifier"/> used where no live overlay connection
/// exists (worker processes, background services, tests). The API host replaces this with the
/// SignalR-backed adapter (<c>EventResponseOverlayNotifierAdapter</c>) via a later service registration.
/// </summary>
internal sealed class NullEventResponseOverlayNotifier : IEventResponseOverlayNotifier
{
    public Task NotifyAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string resolvedMessage,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
