// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Services.Ipc;

/// <summary>
/// The accept seam the IPC dev-mode listener rides — exists so tests drive the full
/// auth → request/response protocol over in-memory connections, no real OS socket
/// (the same testability move as the OBS <c>IObsSocket</c> seam). The production
/// impl binds the OS-local endpoint: a Unix domain socket in the self-host data
/// directory on POSIX, the <c>nomnomzbot-ipc</c> named pipe on Windows — never TCP.
/// </summary>
public interface IIpcListenerFactory
{
    /// <summary>Binds the process-local endpoint and starts accepting.</summary>
    IIpcListener Bind();
}

/// <summary>A bound local listener yielding one <see cref="IIpcConnection"/> per client.</summary>
public interface IIpcListener : IAsyncDisposable
{
    /// <summary>Human-readable endpoint (socket path / pipe name) for logs.</summary>
    string EndpointDescription { get; }

    /// <summary>The next inbound connection, or null once the listener has closed.</summary>
    Task<IIpcConnection?> AcceptAsync(CancellationToken ct);
}

/// <summary>
/// One newline-delimited-JSON duplex connection. Reads are framed (one JSON object
/// per line) with the 64&#160;KB cap enforced by the transport, so the service layer
/// only ever sees whole frames or the overflow verdict.
/// </summary>
public interface IIpcConnection : IAsyncDisposable
{
    Task<IpcReadResult> ReadFrameAsync(CancellationToken ct);

    /// <summary>Writes one JSON frame followed by the newline delimiter.</summary>
    Task WriteFrameAsync(string json, CancellationToken ct);
}

/// <summary>Outcome of one framed read: a frame, a closed peer, or a frame-cap breach.</summary>
public readonly record struct IpcReadResult(string? Frame, bool FrameTooLarge)
{
    public static IpcReadResult Closed => new(null, false);

    public static IpcReadResult TooLarge => new(null, true);

    public static IpcReadResult Of(string frame) => new(frame, false);

    public bool IsClosed => Frame is null && !FrameTooLarge;
}
