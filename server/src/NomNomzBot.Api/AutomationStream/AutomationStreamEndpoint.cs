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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.AutomationApi.Stream;

namespace NomNomzBot.Api.AutomationStream;

/// <summary>
/// The raw WebSocket endpoint at <c>/automation/v1/stream</c> (automation-api.md §4.2/D1 —
/// deliberately NOT SignalR; the surface is language-agnostic third-party tooling). Native clients
/// authenticate on the handshake via <c>Authorization: Bearer</c>; browser clients authenticate with
/// the first-frame <c>authenticate</c> op. The protocol itself lives in
/// <see cref="AutomationStreamCoordinator"/>; this file only upgrades the socket and adapts it.
/// </summary>
public static class AutomationStreamEndpoint
{
    public static void MapAutomationStream(this WebApplication app)
    {
        app.Map(
            "/automation/v1/stream",
            async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                // Native-client handshake auth (D3: header, never query string). A bad header fails
                // the upgrade outright; an ABSENT header falls through to the in-band authenticate op.
                AutomationPrincipal? headerPrincipal = null;
                string? authorization = context.Request.Headers.Authorization;
                if (!string.IsNullOrEmpty(authorization))
                {
                    if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                    IAutomationTokenAuthenticator authenticator =
                        context.RequestServices.GetRequiredService<IAutomationTokenAuthenticator>();
                    Result<AutomationPrincipal> authenticated =
                        await authenticator.AuthenticateAsync(
                            authorization["Bearer ".Length..].Trim(),
                            context.RequestAborted
                        );
                    if (authenticated.IsFailure)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                    headerPrincipal = authenticated.Value;
                }

                using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                AutomationStreamCoordinator coordinator =
                    context.RequestServices.GetRequiredService<AutomationStreamCoordinator>();
                WebSocketStreamConnection connection = new(socket);
                try
                {
                    await coordinator.RunAsync(connection, headerPrincipal, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // Client vanished / server shutting down — nothing to report.
                }
                catch (WebSocketException)
                {
                    // Abrupt client disconnect — routine for long-lived sockets.
                }
            }
        );
    }
}

/// <summary>Adapts a Kestrel <see cref="WebSocket"/> to the coordinator's connection contract; sends are serialized.</summary>
internal sealed class WebSocketStreamConnection : IAutomationStreamConnection
{
    private const int ReceiveBufferBytes = 16 * 1024;
    private const int MaxFrameBytes = 256 * 1024;

    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public WebSocketStreamConnection(WebSocket socket)
    {
        _socket = socket;
    }

    public async Task SendAsync(string frameJson, CancellationToken ct)
    {
        await _sendGate.WaitAsync(ct);
        try
        {
            if (_socket.State != WebSocketState.Open)
                return;
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
                return null; // abrupt disconnect = closed
            }

            if (received.MessageType == WebSocketMessageType.Close)
                return null;
            if (received.MessageType == WebSocketMessageType.Binary)
            {
                // Text-frame protocol (§4.2): binary is a protocol violation, not data.
                await CloseAsync("text frames only", CancellationToken.None);
                return null;
            }

            frame.Write(buffer, 0, received.Count);
            if (frame.Length > MaxFrameBytes)
            {
                await CloseAsync("frame too large", CancellationToken.None);
                return null;
            }
            if (received.EndOfMessage)
                return Encoding.UTF8.GetString(frame.ToArray());
        }
    }

    public async Task CloseAsync(string reason, CancellationToken ct)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, ct);
            }
            catch (WebSocketException)
            {
                // Already gone — closing a dead socket is a no-op.
            }
        }
    }
}
