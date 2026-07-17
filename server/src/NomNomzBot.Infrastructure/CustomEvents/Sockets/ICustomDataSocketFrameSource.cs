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

namespace NomNomzBot.Infrastructure.CustomEvents.Sockets;

/// <summary>
/// The raw transport under the custom-data socket ingress runner (custom-events.md D2 <c>socket</c>): open a
/// stream to a fully-resolved <c>wss://</c> endpoint and yield its inbound text frames until the peer closes or
/// cancellation. A seam so <see cref="CustomDataSocketHostedService"/> is provable without a live provider
/// socket; the production implementation is <see cref="ClientWebSocketDataFrameSource"/>. The auth secret is
/// already baked into <paramref name="endpoint"/> (Pulsoid/HypeRate carry it as a query parameter), so the
/// transport needs only the URI. Transport failures throw — the runner owns backoff/reconnect.
/// </summary>
internal interface ICustomDataSocketFrameSource
{
    IAsyncEnumerable<string> ConnectAndReceiveAsync(Uri endpoint, CancellationToken ct);
}

/// <summary>
/// The production transport: one <see cref="ClientWebSocket"/> per source, frame reassembly across partial
/// receives, and a hard per-frame byte cap (mirrors the ingest raw cap, custom-events.md D4) so a runaway
/// stream can never hand an unbounded — or truncated — payload to ingest. Binary frames are skipped (every
/// supported provider speaks text).
/// </summary>
internal sealed class ClientWebSocketDataFrameSource : ICustomDataSocketFrameSource
{
    private const int ReceiveBufferBytes = 16 * 1024;

    /// <summary>Per-frame cap — mirrors <c>CustomDataIngestService.MaxRawPayloadBytes</c> (64 KB, spec D4).</summary>
    private const int MaxFrameBytes = 64 * 1024;

    public async IAsyncEnumerable<string> ConnectAndReceiveAsync(
        Uri endpoint,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(endpoint, ct);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferBytes);
        try
        {
            // Frames reassemble as BYTES and decode once complete — decoding per chunk would tear a
            // multibyte UTF-8 character split across two receives.
            using MemoryStream frame = new();
            bool overCap = false;
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

                // Over-cap frames are drained to their end and dropped whole — never yielded as a truncated
                // fragment that ingest would treat as a complete payload.
                if (overCap || frame.Length + result.Count > MaxFrameBytes)
                    overCap = true;
                else
                    frame.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage)
                    continue;

                if (overCap)
                {
                    frame.SetLength(0);
                    overCap = false;
                    continue;
                }

                string complete = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
                frame.SetLength(0);
                yield return complete;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
