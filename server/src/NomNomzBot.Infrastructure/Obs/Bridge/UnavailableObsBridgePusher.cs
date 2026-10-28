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

using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>
/// Fallback pusher so the Infrastructure provider validates standalone (tests, tooling). The API
/// host REPLACES this with the SignalR-backed pusher; it is unreachable in practice because a
/// bridge can only become leader by connecting through that same host.
/// </summary>
public sealed class UnavailableObsBridgePusher : IObsBridgePusher
{
    public Task PushExecuteAsync(
        string connectionId,
        Guid commandId,
        string payloadJson,
        CancellationToken ct = default
    ) =>
        throw new InvalidOperationException(
            "OBS bridge pushes require the API host (SignalR hub) to be running."
        );
}
