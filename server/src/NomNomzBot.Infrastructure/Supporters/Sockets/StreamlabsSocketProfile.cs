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
/// Streamlabs' Socket.IO dialect (supporter-events.md §0 D3, verified against dev.streamlabs.com): connect to
/// <c>https://sockets.streamlabs.com?token=SOCKET_TOKEN</c> (their server speaks Socket.IO v2 → Engine.IO 3)
/// and listen on the single <c>event</c> event. A <c>donation</c> event carries its donations in a
/// <c>message</c> ARRAY — each item is one ingestable payload (one tip each, own id/amount/currency). Every
/// other event type (platform follows/subs the socket also carries) is not a supporter payload here and is
/// ignored.
/// </summary>
internal sealed class StreamlabsSocketProfile : ISocketIoProfile
{
    public string SourceKey => "streamlabs";

    public int EngineIoVersion => 3; // Streamlabs runs a Socket.IO v2 server

    public IReadOnlyList<string> EventNames { get; } = ["event"];

    public Uri BuildUri(string secret) =>
        new($"https://sockets.streamlabs.com?token={Uri.EscapeDataString(secret)}");

    /// <summary>Static endpoint — the socket token rides the query; no discovery, no post-connect emit.</summary>
    public ValueTask<Uri> ResolveEndpointAsync(
        string secret,
        HttpClient http,
        CancellationToken ct
    ) => ValueTask.FromResult(BuildUri(secret));

    public SocketIoEmit? BuildConnectEmit(string secret) => null;

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

        // The frame is the Socket.IO args array — the event object is its first element.
        JObject? evt = parsed switch
        {
            JArray { Count: > 0 } args => args[0] as JObject,
            JObject direct => direct,
            _ => null,
        };
        if (
            evt is null
            || !string.Equals(evt.Value<string>("type"), "donation", StringComparison.Ordinal)
            || evt["message"] is not JArray donations
        )
            return [];

        List<string> payloads = [];
        foreach (JToken donation in donations)
            if (donation is JObject item)
                payloads.Add(item.ToString(Formatting.None));
        return payloads;
    }
}
