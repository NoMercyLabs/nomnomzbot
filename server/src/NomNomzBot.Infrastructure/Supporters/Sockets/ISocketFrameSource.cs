// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace NomNomzBot.Infrastructure.Supporters.Sockets;

/// <summary>
/// The raw transport under the socket ingress runner: open a stream and yield its inbound text frames. A seam
/// so <see cref="SupporterSocketHostedService"/> is provable without a live provider socket; the production
/// implementation is <see cref="ClientWebSocketFrameSource"/>.
/// </summary>
internal interface ISocketFrameSource
{
    /// <summary>
    /// Opens the stream <paramref name="profile"/> describes for a connection's decrypted
    /// <paramref name="secret"/> (endpoint resolution — including any HTTP discovery — belongs to the
    /// transport) and yields each inbound frame until the peer closes or <paramref name="ct"/> cancels.
    /// Raw-WS profiles yield wire text frames; Socket.IO profiles yield each subscribed event's argument
    /// array as JSON. Transport failures throw — the runner owns backoff/reconnect.
    /// </summary>
    IAsyncEnumerable<string> ConnectAndReceiveAsync(
        ISupporterSocketProfile profile,
        string secret,
        CancellationToken ct
    );
}

/// <summary>Routes each profile to its transport: raw WebSocket or Socket.IO.</summary>
internal sealed class CompositeFrameSource(
    ClientWebSocketFrameSource rawWebSocket,
    SocketIoFrameSource socketIo
) : ISocketFrameSource
{
    public IAsyncEnumerable<string> ConnectAndReceiveAsync(
        ISupporterSocketProfile profile,
        string secret,
        CancellationToken ct
    ) =>
        profile switch
        {
            IRawWebSocketProfile => rawWebSocket.ConnectAndReceiveAsync(profile, secret, ct),
            ISocketIoProfile => socketIo.ConnectAndReceiveAsync(profile, secret, ct),
            _ => throw new NotSupportedException(
                $"Socket profile '{profile.SourceKey}' declares no transport interface."
            ),
        };
}

/// <summary>
/// The raw-WS transport: one <see cref="ClientWebSocket"/> per stream, a timer-driven text keepalive, and
/// frame reassembly across partial receives. Binary frames are skipped (every supported provider speaks text).
/// </summary>
internal sealed class ClientWebSocketFrameSource : ISocketFrameSource
{
    private const int ReceiveBufferBytes = 16 * 1024;

    public async IAsyncEnumerable<string> ConnectAndReceiveAsync(
        ISupporterSocketProfile profile,
        string secret,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        IRawWebSocketProfile raw = (IRawWebSocketProfile)profile;
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(raw.BuildUri(secret), ct);

        // The keepalive writes concurrently with the receive loop — WebSocket allows one send + one receive
        // in flight, which is exactly this shape.
        using CancellationTokenSource keepaliveCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task keepalive =
            raw.KeepaliveInterval is TimeSpan interval && raw.KeepalivePayload is string payload
                ? SendKeepalivesAsync(socket, interval, payload, keepaliveCts.Token)
                : Task.CompletedTask;

        try
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferBytes);
            try
            {
                // Frames reassemble as BYTES and decode once complete — decoding per chunk would tear a
                // multibyte UTF-8 character split across two receives.
                using MemoryStream frame = new();
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        ct
                    );
                    if (result.MessageType == WebSocketMessageType.Close)
                        yield break;
                    if (result.MessageType != WebSocketMessageType.Text)
                        continue; // binary frames carry nothing we ingest.

                    frame.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    string complete = Encoding.UTF8.GetString(
                        frame.GetBuffer(),
                        0,
                        (int)frame.Length
                    );
                    frame.SetLength(0);
                    yield return complete;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            await keepaliveCts.CancelAsync();
            try
            {
                await keepalive;
            }
            catch (OperationCanceledException)
            {
                // The keepalive loop ends by cancellation — expected.
            }
        }
    }

    private static async Task SendKeepalivesAsync(
        ClientWebSocket socket,
        TimeSpan interval,
        string payload,
        CancellationToken ct
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        using PeriodicTimer timer = new(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (socket.State != WebSocketState.Open)
                return;
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct
            );
        }
    }
}
