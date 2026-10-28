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

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>One bridge ack, raw off the wire — each transport shapes it for its own protocol.</summary>
public sealed record ObsBridgeAck(bool Ok, string? DataJson, string? Error);

/// <summary>
/// In-flight bridge commands (obs-control.md D3): a transport begins a command, the hub's
/// <c>AckCommand</c> completes it. Shared by the OBS AND VTS bridge transports (one relay carries
/// both — vtube-studio.md D1). Per-node by design — the ack always lands on the node that pushed
/// (the bridge answers over its own socket). A duplicate ack for an unknown/settled id is a no-op
/// (idempotent CommandId). Each command remembers its channel, and only a bridge of that same channel
/// can settle it — another channel's bridge cannot forge an answer.
/// </summary>
public sealed class ObsBridgeCommandBook
{
    private sealed record Pending(Guid BroadcasterId, TaskCompletionSource<ObsBridgeAck> Answer);

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    public Task<ObsBridgeAck> BeginAsync(Guid broadcasterId, Guid commandId)
    {
        TaskCompletionSource<ObsBridgeAck> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[commandId] = new(broadcasterId, tcs);
        return tcs.Task;
    }

    public bool Complete(Guid broadcasterId, Guid commandId, ObsBridgeAck ack)
    {
        if (
            !_pending.TryGetValue(commandId, out Pending? pending)
            || pending.BroadcasterId != broadcasterId
            || !_pending.TryRemove(new KeyValuePair<Guid, Pending>(commandId, pending))
        )
            return false;
        return pending.Answer.TrySetResult(ack);
    }

    public void Abandon(Guid commandId)
    {
        if (_pending.TryRemove(commandId, out Pending? pending))
            pending.Answer.TrySetCanceled();
    }
}
