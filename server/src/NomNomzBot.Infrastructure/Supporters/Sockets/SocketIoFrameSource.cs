// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SocketIOClient;

namespace NomNomzBot.Infrastructure.Supporters.Sockets;

/// <summary>
/// The Socket.IO transport for supporter live sockets (supporter-events.md D3): resolves the profile's
/// endpoint (including any HTTP host discovery, e.g. Tipeee's <c>/v2.0/site/socket</c>), connects one
/// <see cref="SocketIOClient.SocketIO"/>, sends the profile's post-connect authentication emit when it has
/// one (StreamElements' <c>authenticate</c>, Tipeee's <c>join-room</c>), and subscribes the named events —
/// each received event yields its argument array as JSON for the profile's <c>TranslateFrame</c>. The
/// client's own reconnection is OFF — the ingress runner owns backoff/reconnect, exactly like the raw-WS
/// transport. The Engine.IO protocol comes from the profile (3 for a Socket.IO-v2 server; 4 for v3/v4).
/// </summary>
internal sealed class SocketIoFrameSource(IHttpClientFactory httpClientFactory) : ISocketFrameSource
{
    internal const string HttpClientName = "supporter-socket-discovery";

    public async IAsyncEnumerable<string> ConnectAndReceiveAsync(
        ISupporterSocketProfile profile,
        string secret,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        ISocketIoProfile io = (ISocketIoProfile)profile;
        Uri endpoint = await io.ResolveEndpointAsync(
            secret,
            httpClientFactory.CreateClient(HttpClientName),
            ct
        );

        Channel<string> frames = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true }
        );

        using SocketIO client = new(
            endpoint,
            new SocketIOOptions
            {
                EIO = (SocketIOClient.Common.EngineIO)io.EngineIoVersion,
                Reconnection = false, // the ingress runner owns backoff/reconnect
                ConnectionTimeout = TimeSpan.FromSeconds(20),
            }
        );

        foreach (string eventName in io.EventNames)
            client.On(
                eventName,
                ctx =>
                {
                    frames.Writer.TryWrite(ctx.RawText); // the args array as JSON
                    return Task.CompletedTask;
                }
            );
        client.OnDisconnected += (_, reason) =>
            frames.Writer.TryComplete(
                new IOException($"Socket.IO stream disconnected ({reason}).")
            );

        await client.ConnectAsync(ct);
        if (io.BuildConnectEmit(secret) is SocketIoEmit emit)
            await client.EmitAsync(emit.EventName, [emit.Payload], ct);

        try
        {
            await foreach (string frame in frames.Reader.ReadAllAsync(ct))
                yield return frame;
        }
        finally
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch
            {
                // Tear-down only — the socket may already be gone.
            }
        }
    }
}
