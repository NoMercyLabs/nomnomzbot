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
/// StreamElements' Socket.IO dialect (supporter-events.md §0 D3, verified against
/// StreamElements/api-docs Websockets.md): connect to <c>https://realtime.streamelements.com</c> (their
/// server speaks Socket.IO v2 → Engine.IO 3), authenticate post-connect with
/// <c>authenticate {method:"jwt", token}</c> — the connection secret is the account's JWT — and listen on
/// <c>event</c>. Only <c>type == "tip"</c> events ingest; the same socket also carries follows/subs/cheers,
/// which are platform facts EventSub already owns here, not supporter payloads.
/// </summary>
internal sealed class StreamelementsSocketProfile : ISocketIoProfile
{
    public string SourceKey => "streamelements";

    public int EngineIoVersion => 3; // StreamElements runs a Socket.IO v2 server

    public IReadOnlyList<string> EventNames { get; } = ["event"];

    public Uri BuildUri(string secret) => new("https://realtime.streamelements.com");

    public ValueTask<Uri> ResolveEndpointAsync(
        string secret,
        HttpClient http,
        CancellationToken ct
    ) => ValueTask.FromResult(BuildUri(secret));

    /// <summary>The token never rides the query — it authenticates via the post-connect emit.</summary>
    public SocketIoEmit? BuildConnectEmit(string secret) =>
        new("authenticate", new { method = "jwt", token = secret });

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
            || !string.Equals(evt.Value<string>("type"), "tip", StringComparison.Ordinal)
        )
            return [];

        return [evt.ToString(Formatting.None)];
    }
}
