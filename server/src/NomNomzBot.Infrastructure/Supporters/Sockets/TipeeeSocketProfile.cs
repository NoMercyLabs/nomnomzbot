// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.Supporters.Sockets;

/// <summary>
/// TipeeeStream's Socket.IO dialect (supporter-events.md §0 D3, verified against
/// api.tipeeestream.com/api-doc/socketio): the socket host is DISCOVERED via
/// <c>GET /v2.0/site/socket</c> (→ <c>datas.host:port</c>, currently <c>sso-cf.tipeeestream.com:443</c> —
/// kept as the fallback when discovery fails), the API key rides the <c>access_token</c> query, a
/// post-connect <c>join-room {room: apiKey, username}</c> subscribes the stream (the room IS the key; the
/// username is a client label, not a credential), and <c>new-event</c> frames carry the events — only
/// <c>event.type == "donation"</c> ingests.
/// </summary>
internal sealed class TipeeeSocketProfile : ISocketIoProfile
{
    private const string DiscoveryUrl = "https://api.tipeeestream.com/v2.0/site/socket";

    public string SourceKey => "tipeee";

    public int EngineIoVersion => 3; // Tipeee's SSO socket speaks Socket.IO v2

    public IReadOnlyList<string> EventNames { get; } = ["new-event"];

    /// <summary>The documented current host — the fallback when discovery is unreachable.</summary>
    public Uri BuildUri(string secret) =>
        new($"https://sso-cf.tipeeestream.com:443?access_token={Uri.EscapeDataString(secret)}");

    public async ValueTask<Uri> ResolveEndpointAsync(
        string secret,
        HttpClient http,
        CancellationToken ct
    )
    {
        try
        {
            string body = await http.GetStringAsync(DiscoveryUrl, ct);
            JObject parsed = JObject.Parse(body);
            string? host = parsed["datas"]?.Value<string>("host");
            string? port = parsed["datas"]?.Value<string>("port");
            if (!string.IsNullOrWhiteSpace(host))
                return new Uri(
                    $"{host.TrimEnd('/')}:{(string.IsNullOrWhiteSpace(port) ? "443" : port)}"
                        + $"?access_token={Uri.EscapeDataString(secret)}"
                );
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or UriFormatException)
        {
            // Discovery down/odd — fall through to the documented static host.
        }
        return BuildUri(secret);
    }

    /// <summary>The room IS the API key; the username is a client presence label, not a credential.</summary>
    public SocketIoEmit? BuildConnectEmit(string secret) =>
        new("join-room", new { room = secret, username = "nomnomzbot" });

    public IReadOnlyList<string> TranslateFrame(string frame)
    {
        JToken parsed;
        try
        {
            parsed = JToken.Parse(frame);
        }
        catch (JsonException)
        {
            return [];
        }

        // The frame is the Socket.IO args array: [{ appKey, event: {...} }].
        JObject? wrapper = parsed switch
        {
            JArray { Count: > 0 } args => args[0] as JObject,
            JObject direct => direct,
            _ => null,
        };
        if (
            wrapper?["event"] is not JObject evt
            || !string.Equals(evt.Value<string>("type"), "donation", StringComparison.Ordinal)
        )
            return [];

        return [evt.ToString(Formatting.None)];
    }
}
