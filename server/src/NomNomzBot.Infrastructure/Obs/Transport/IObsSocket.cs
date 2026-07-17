// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.WebSockets;
using System.Text;

namespace NomNomzBot.Infrastructure.Obs.Transport;

/// <summary>
/// The duplex text-frame seam the direct OBS transport speaks over — exists so tests drive the full
/// Hello → Identify → request/response/event protocol over an in-memory pair, no real socket.
/// (The EventSub <c>IWebSocketChannel</c> seam is receive-only, so OBS gets its own.)
/// </summary>
public interface IObsSocket : IAsyncDisposable
{
    Task SendAsync(string frameJson, CancellationToken ct);

    /// <summary>The next text frame, or null when the socket closed.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);
}

/// <summary>Dials an OBS-WebSocket endpoint. The production impl wraps <see cref="ClientWebSocket"/>.</summary>
public interface IObsSocketFactory
{
    Task<IObsSocket> ConnectAsync(Uri uri, CancellationToken ct);
}

/// <summary>Production factory over <see cref="ClientWebSocket"/> with per-connection send serialization.</summary>
public sealed class ClientObsSocketFactory : IObsSocketFactory
{
    public async Task<IObsSocket> ConnectAsync(Uri uri, CancellationToken ct)
    {
        ClientWebSocket socket = new();
        // obs-websocket offers msgpack too; we always speak JSON.
        socket.Options.AddSubProtocol("obswebsocket.json");
        await socket.ConnectAsync(uri, ct);
        return new ClientObsSocket(socket);
    }

    private sealed class ClientObsSocket : IObsSocket
    {
        private const int ReceiveBufferBytes = 32 * 1024;

        private readonly ClientWebSocket _socket;
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public ClientObsSocket(ClientWebSocket socket)
        {
            _socket = socket;
        }

        public async Task SendAsync(string frameJson, CancellationToken ct)
        {
            await _sendGate.WaitAsync(ct);
            try
            {
                await _socket.SendAsync(
                    Encoding.UTF8.GetBytes(frameJson),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct
                );
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[ReceiveBufferBytes];
            using MemoryStream frame = new();
            while (true)
            {
                WebSocketReceiveResult received;
                try
                {
                    received = await _socket.ReceiveAsync(buffer, ct);
                }
                catch (WebSocketException)
                {
                    return null;
                }

                if (received.MessageType == WebSocketMessageType.Close)
                    return null;
                frame.Write(buffer, 0, received.Count);
                if (received.EndOfMessage)
                    return Encoding.UTF8.GetString(frame.ToArray());
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "shutdown",
                        CancellationToken.None
                    );
            }
            catch (WebSocketException)
            {
                // Already gone.
            }
            finally
            {
                _socket.Dispose();
            }
        }
    }
}
