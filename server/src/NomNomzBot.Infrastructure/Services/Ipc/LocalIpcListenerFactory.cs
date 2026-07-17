// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO.Pipes;
using System.Net.Sockets;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Services.Ipc;

/// <summary>
/// Production bind for the IPC dev-mode listener (stream-admin.md §7): a <b>process-local endpoint
/// only</b>, never TCP. On Windows that is the <c>nomnomzbot-ipc</c> named pipe; on Linux/macOS a
/// Unix domain socket at <c>&lt;data-dir&gt;/ipc.sock</c> (the <see cref="SelfHostDataPaths"/> home,
/// so the socket lives beside the SQLite file and vault keys, overridable via
/// <c>NOMNOMZ_DATA_DIR</c>). A stale socket file from a crashed run is deleted before rebinding;
/// clean shutdown removes it again.
/// </summary>
public sealed class LocalIpcListenerFactory : IIpcListenerFactory
{
    /// <summary>The well-known Windows pipe name local dev tooling dials (<c>\\.\pipe\nomnomzbot-ipc</c>).</summary>
    public const string PipeName = "nomnomzbot-ipc";

    /// <summary>File name of the POSIX Unix-domain socket inside the data directory.</summary>
    public const string SocketFileName = "ipc.sock";

    public IIpcListener Bind() =>
        OperatingSystem.IsWindows()
            ? new NamedPipeIpcListener()
            : UnixSocketIpcListener.Bind(
                Path.Combine(SelfHostDataPaths.BaseDirectory, SocketFileName)
            );

    /// <summary>Windows branch — one <see cref="NamedPipeServerStream"/> instance per accepted client.</summary>
    private sealed class NamedPipeIpcListener : IIpcListener
    {
        private volatile bool _disposed;
        private NamedPipeServerStream? _waiting;

        public string EndpointDescription => $@"\\.\pipe\{PipeName}";

        public async Task<IIpcConnection?> AcceptAsync(CancellationToken ct)
        {
            if (_disposed)
                return null;

            NamedPipeServerStream pipe = new(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );
            _waiting = pipe;
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                return new StreamIpcConnection(pipe);
            }
            catch (Exception) when (_disposed || ct.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                return null;
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
            finally
            {
                _waiting = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            if (_waiting is { } pipe)
                await pipe.DisposeAsync();
        }
    }

    /// <summary>POSIX branch — a Unix domain socket file in the self-host data directory.</summary>
    private sealed class UnixSocketIpcListener : IIpcListener
    {
        private readonly Socket _socket;
        private readonly string _socketPath;
        private volatile bool _disposed;

        private UnixSocketIpcListener(Socket socket, string socketPath)
        {
            _socket = socket;
            _socketPath = socketPath;
        }

        public string EndpointDescription => _socketPath;

        public static UnixSocketIpcListener Bind(string socketPath)
        {
            // A previous run that died hard leaves the socket file behind; the bind would fail on it.
            if (File.Exists(socketPath))
                File.Delete(socketPath);

            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            socket.Listen(backlog: 8);
            return new UnixSocketIpcListener(socket, socketPath);
        }

        public async Task<IIpcConnection?> AcceptAsync(CancellationToken ct)
        {
            try
            {
                Socket client = await _socket.AcceptAsync(ct);
                return new StreamIpcConnection(new NetworkStream(client, ownsSocket: true));
            }
            catch (Exception) when (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            _socket.Dispose();
            try
            {
                if (File.Exists(_socketPath))
                    File.Delete(_socketPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup — a leftover file is re-deleted on the next bind.
            }
            return ValueTask.CompletedTask;
        }
    }
}
