// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
