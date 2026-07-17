// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Obs.Dtos;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>
/// In-flight bridge commands (obs-control.md D3): the transport begins a command, the hub's
/// <c>AckCommand</c> completes it. Per-node by design — the ack always lands on the node that pushed
/// (the bridge answers over its own socket). A duplicate ack for an unknown/settled id is a no-op
/// (idempotent CommandId).
/// </summary>
public sealed class ObsBridgeCommandBook
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ObsResponse>> _pending = new();

    public Task<ObsResponse> BeginAsync(Guid commandId)
    {
        TaskCompletionSource<ObsResponse> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[commandId] = tcs;
        return tcs.Task;
    }

    public bool Complete(Guid commandId, ObsResponse response)
    {
        if (!_pending.TryRemove(commandId, out TaskCompletionSource<ObsResponse>? tcs))
            return false;
        return tcs.TrySetResult(response);
    }

    public void Abandon(Guid commandId)
    {
        if (_pending.TryRemove(commandId, out TaskCompletionSource<ObsResponse>? tcs))
            tcs.TrySetCanceled();
    }
}
