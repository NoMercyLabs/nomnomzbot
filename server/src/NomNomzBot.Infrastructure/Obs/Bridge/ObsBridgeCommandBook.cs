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

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>One bridge ack, raw off the wire — each transport shapes it for its own protocol.</summary>
public sealed record ObsBridgeAck(bool Ok, string? DataJson, string? Error);

/// <summary>
/// In-flight bridge commands (obs-control.md D3): a transport begins a command, the hub's
/// <c>AckCommand</c> completes it. Shared by the OBS AND VTS bridge transports (one relay carries
/// both — vtube-studio.md D1). Per-node by design — the ack always lands on the node that pushed
/// (the bridge answers over its own socket). A duplicate ack for an unknown/settled id is a no-op
/// (idempotent CommandId).
/// </summary>
public sealed class ObsBridgeCommandBook
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ObsBridgeAck>> _pending =
        new();

    public Task<ObsBridgeAck> BeginAsync(Guid commandId)
    {
        TaskCompletionSource<ObsBridgeAck> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[commandId] = tcs;
        return tcs.Task;
    }

    public bool Complete(Guid commandId, ObsBridgeAck ack)
    {
        if (!_pending.TryRemove(commandId, out TaskCompletionSource<ObsBridgeAck>? tcs))
            return false;
        return tcs.TrySetResult(ack);
    }

    public void Abandon(Guid commandId)
    {
        if (_pending.TryRemove(commandId, out TaskCompletionSource<ObsBridgeAck>? tcs))
            tcs.TrySetCanceled();
    }
}
