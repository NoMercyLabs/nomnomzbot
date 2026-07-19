// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Infrastructure.Obs;
using NomNomzBot.Infrastructure.Obs.Transport;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// The real-wire counterpart to <see cref="DirectObsTransportTests"/> (which drives the protocol over
/// an in-memory <c>FakeObsSocket</c>): this exercises the PRODUCTION <see cref="ClientObsSocketFactory"/>
/// — the actual <see cref="ClientWebSocket"/> wrapper that talks to a self-hosted OBS — against a mock
/// obs-websocket v5 server bound to a real localhost TCP port. It proves the last mile no unit test
/// covers: the subprotocol negotiation, the RFC6455 frame round-trip, and the Hello → Identify →
/// request/response handshake all working over a genuine socket, so "OBS control (local host)" is
/// verified end-to-end and a wire-level regression fails this test.
/// </summary>
public sealed class ObsRealSocketIntegrationTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7a2");

    [Fact]
    public async Task Direct_transport_talks_to_a_real_obs_socket_end_to_end()
    {
        await using MockObsWebSocketServer server = await MockObsWebSocketServer.StartAsync();

        DirectObsTransport transport = BuildTransport(server.Port);

        Result<ObsResponse> result = await transport
            .SendAsync(
                Channel,
                Guid.CreateVersion7(),
                new ObsRequest(
                    "SetCurrentProgramScene",
                    new Dictionary<string, object?> { ["sceneName"] = "Starting Soon" }
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Ok.Should().BeTrue();

        // The mock captured the request off the real wire — exact type + payload the transport framed.
        ObsWireRequest received = await server.WaitForRequestAsync(TimeSpan.FromSeconds(5));
        received.RequestType.Should().Be("SetCurrentProgramScene");
        received.Data.GetProperty("sceneName").GetString().Should().Be("Starting Soon");

        // And the transport observed the server's Identify handshake before any request.
        server
            .Identified.Should()
            .BeTrue("the client must complete Hello → Identify → Identified");
    }

    private static DirectObsTransport BuildTransport(int port)
    {
        ObsTestDbContext db = ObsTestDbContext.New();
        db.ObsConnections.Add(
            new ObsConnection
            {
                BroadcasterId = Channel,
                Mode = "direct",
                IsEnabled = true,
                Host = "127.0.0.1",
                Port = port,
                PasswordCipher = null,
                EventSubscriptionsMask = 0,
            }
        );
        db.SaveChanges();

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string?>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((string?)null);

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(protector);
        services.AddScoped<IObsConnectionService, ObsConnectionService>();
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new DirectObsTransport(
            new ClientObsSocketFactory(),
            scopeFactory,
            new RecordingEventBus(),
            new FakeTimeProvider(),
            NullLogger<DirectObsTransport>.Instance
        );
    }

    /// <summary>A captured op-6 request the mock server received off the real socket.</summary>
    private sealed record ObsWireRequest(string RequestType, JsonElement Data);

    /// <summary>
    /// A minimal obs-websocket v5 server over a raw TCP socket: does the RFC6455 upgrade by hand
    /// (echoing the <c>obswebsocket.json</c> subprotocol so <see cref="ClientWebSocket"/> accepts it),
    /// then speaks Hello (op 0) → waits for Identify (op 1) → Identified (op 2) → answers each Request
    /// (op 6) with a success RequestResponse (op 7). No auth (the happy self-host path).
    /// </summary>
    private sealed class MockObsWebSocketServer : IAsyncDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<ObsWireRequest> _firstRequest = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private Task? _loop;

        public int Port { get; }
        public bool Identified { get; private set; }

        private MockObsWebSocketServer(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        public static Task<MockObsWebSocketServer> StartAsync()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            MockObsWebSocketServer server = new(listener, port);
            server._loop = Task.Run(() => server.AcceptAsync(server._cts.Token));
            return Task.FromResult(server);
        }

        public async Task<ObsWireRequest> WaitForRequestAsync(TimeSpan timeout) =>
            await _firstRequest.Task.WaitAsync(timeout);

        private async Task AcceptAsync(CancellationToken ct)
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                await using NetworkStream stream = client.GetStream();
                string key = await ReadUpgradeKeyAsync(stream, ct);
                await WriteHandshakeAsync(stream, key, ct);

                using WebSocket ws = WebSocket.CreateFromStream(
                    stream,
                    isServer: true,
                    subProtocol: "obswebsocket.json",
                    keepAliveInterval: TimeSpan.FromSeconds(30)
                );

                await SendJsonAsync(
                    ws,
                    """{ "op": 0, "d": { "obsWebSocketVersion": "5.5.0", "rpcVersion": 1 } }""",
                    ct
                );

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    string? frame = await ReceiveJsonAsync(ws, ct);
                    if (frame is null)
                        break;
                    using JsonDocument doc = JsonDocument.Parse(frame);
                    int op = doc.RootElement.GetProperty("op").GetInt32();
                    if (op == 1)
                    {
                        Identified = true;
                        await SendJsonAsync(
                            ws,
                            """{ "op": 2, "d": { "negotiatedRpcVersion": 1 } }""",
                            ct
                        );
                    }
                    else if (op == 6)
                    {
                        JsonElement d = doc.RootElement.GetProperty("d");
                        string requestType = d.GetProperty("requestType").GetString()!;
                        string requestId = d.GetProperty("requestId").GetString()!;
                        JsonElement data = d.TryGetProperty("requestData", out JsonElement rd)
                            ? rd.Clone()
                            : default;
                        _firstRequest.TrySetResult(new ObsWireRequest(requestType, data));
                        await SendJsonAsync(
                            ws,
                            $$"""
                            { "op": 7, "d": { "requestType": "{{requestType}}", "requestId": "{{requestId}}", "requestStatus": { "result": true, "code": 100 } } }
                            """,
                            ct
                        );
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _firstRequest.TrySetException(ex);
            }
        }

        private static async Task<string> ReadUpgradeKeyAsync(
            NetworkStream stream,
            CancellationToken ct
        )
        {
            byte[] buffer = new byte[4096];
            StringBuilder sb = new();
            while (!sb.ToString().Contains("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0)
                    throw new IOException("Client closed during handshake.");
                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            foreach (string line in sb.ToString().Split("\r\n"))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    return line["Sec-WebSocket-Key:".Length..].Trim();
            }
            throw new IOException("No Sec-WebSocket-Key in the upgrade request.");
        }

        private static async Task WriteHandshakeAsync(
            NetworkStream stream,
            string key,
            CancellationToken ct
        )
        {
            string accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid))
            );
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Protocol: obswebsocket.json\r\n"
                + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }

        private static async Task SendJsonAsync(WebSocket ws, string json, CancellationToken ct) =>
            await ws.SendAsync(
                Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct
            );

        private static async Task<string?> ReceiveJsonAsync(WebSocket ws, CancellationToken ct)
        {
            byte[] buffer = new byte[16 * 1024];
            using MemoryStream frame = new();
            while (true)
            {
                WebSocketReceiveResult received = await ws.ReceiveAsync(buffer, ct);
                if (received.MessageType == WebSocketMessageType.Close)
                    return null;
                frame.Write(buffer, 0, received.Count);
                if (received.EndOfMessage)
                    return Encoding.UTF8.GetString(frame.ToArray());
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            if (_loop is not null)
            {
                try
                {
                    await _loop;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
    }
}
