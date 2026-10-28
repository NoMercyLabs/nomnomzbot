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

using System.Net.WebSockets;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The minimal duplex-channel seam the WebSocket transport reads from — the part of <see cref="ClientWebSocket"/>
/// the receive loop actually uses. Exists so tests drive the full connect → welcome → notification → reconnect
/// lifecycle over an in-memory channel without a real socket or Twitch.
/// </summary>
public interface IWebSocketChannel : IAsyncDisposable
{
    Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken
    );
}

/// <summary>Connects an <see cref="IWebSocketChannel"/> to a URL. The production impl wraps <see cref="ClientWebSocket"/>.</summary>
public interface IWebSocketChannelFactory
{
    Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>The production channel: a real <see cref="ClientWebSocket"/> connected to Twitch's EventSub endpoint.</summary>
public sealed class ClientWebSocketChannelFactory : IWebSocketChannelFactory
{
    public async Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ClientWebSocket socket = new();
        await socket.ConnectAsync(uri, cancellationToken);
        return new ClientWebSocketChannel(socket);
    }

    private sealed class ClientWebSocketChannel(ClientWebSocket socket) : IWebSocketChannel
    {
        public Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        ) => socket.ReceiveAsync(buffer, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (
                    socket.State == WebSocketState.Open
                    || socket.State == WebSocketState.CloseReceived
                )
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "shutdown",
                        CancellationToken.None
                    );
            }
            catch (WebSocketException) { }
            finally
            {
                socket.Dispose();
            }
        }
    }
}
