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
/// Pally.gg's raw-WebSocket dialect (supporter-events.md §0 D3, verified against docs.pally.gg): connect to
/// <c>wss://events.pally.gg?auth=KEY&amp;channel=firehose</c>, send a plain <c>ping</c> every 60 s (the server
/// answers <c>pong</c>), and ingest only <c>campaigntip.notify</c> frames — their <c>payload</c> object (the
/// <c>campaignTip</c> + <c>page</c>) is what <c>PallySupporterSource</c> normalizes. Everything else on the
/// wire (the pong echo, future event types) is ignored, never an error.
/// </summary>
internal sealed class PallySocketProfile : ISupporterSocketProfile
{
    private const string TipEventType = "campaigntip.notify";

    public string SourceKey => "pally";

    public TimeSpan? KeepaliveInterval { get; } = TimeSpan.FromSeconds(60);

    public string? KeepalivePayload => "ping";

    public Uri BuildUri(string secret) =>
        new($"wss://events.pally.gg?auth={Uri.EscapeDataString(secret)}&channel=firehose");

    public IReadOnlyList<string> TranslateFrame(string frame)
    {
        JObject? parsed;
        try
        {
            parsed = JToken.Parse(frame) as JObject;
        }
        catch (JsonException)
        {
            return []; // pong echoes and any non-JSON noise are not events.
        }

        if (
            parsed is null
            || !string.Equals(parsed.Value<string>("type"), TipEventType, StringComparison.Ordinal)
            || parsed["payload"] is not JObject payload
        )
            return [];

        return [payload.ToString(Formatting.None)];
    }
}
