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
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// Calls one dashboard-hub method the way the dashboard does — negotiate, open the WebSocket with the token on
/// the query string, SignalR JSON handshake, one invocation — and returns the completion's result. Speaks the
/// wire protocol directly, so the hub runs behind the real bearer pipeline without a SignalR client package.
/// </summary>
internal static class DashboardHubProbe
{
    private const char RecordSeparator = '\u001e';

    public static async Task<HttpStatusCode> NegotiateAsync(TestServer server, string accessToken)
    {
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            $"/hubs/dashboard/negotiate?negotiateVersion=1&access_token={accessToken}",
            null
        );
        return response.StatusCode;
    }

    public static async Task<JsonNode?> InvokeAsync(
        TestServer server,
        string accessToken,
        string method,
        params object[] arguments
    )
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage negotiate = await client.PostAsync(
            $"/hubs/dashboard/negotiate?negotiateVersion=1&access_token={accessToken}",
            null,
            timeout.Token
        );
        negotiate.EnsureSuccessStatusCode();
        string connectionToken = JsonNode.Parse(
            await negotiate.Content.ReadAsStringAsync(timeout.Token)
        )!["connectionToken"]!.GetValue<string>();

        using WebSocket socket = await server
            .CreateWebSocketClient()
            .ConnectAsync(
                new(
                    $"ws://localhost/hubs/dashboard?id={connectionToken}&access_token={accessToken}"
                ),
                timeout.Token
            );
        await SendAsync(socket, """{"protocol":"json","version":1}""", timeout.Token);
        await ReceiveAsync(socket, timeout.Token);

        string invocation = JsonSerializer.Serialize(
            new
            {
                type = 1,
                invocationId = "1",
                target = method,
                arguments,
            }
        );
        await SendAsync(socket, invocation, timeout.Token);

        while (true)
            foreach (string message in await ReceiveAsync(socket, timeout.Token))
            {
                JsonNode frame = JsonNode.Parse(message)!;
                if (frame["type"]!.GetValue<int>() != 3)
                    continue;
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
                return frame["error"] is null
                    ? frame["result"]
                    : throw new InvalidOperationException(frame["error"]!.GetValue<string>());
            }
    }

    private static Task SendAsync(WebSocket socket, string message, CancellationToken ct) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(message + RecordSeparator),
            WebSocketMessageType.Text,
            true,
            ct
        );

    private static async Task<List<string>> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream received = new();
        WebSocketReceiveResult chunk;
        do
        {
            chunk = await socket.ReceiveAsync(buffer, ct);
            received.Write(buffer, 0, chunk.Count);
        } while (!chunk.EndOfMessage);

        return
        [
            .. Encoding
                .UTF8.GetString(received.ToArray())
                .Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries),
        ];
    }
}
