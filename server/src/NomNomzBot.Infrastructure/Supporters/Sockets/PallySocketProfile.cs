// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
internal sealed class PallySocketProfile : IRawWebSocketProfile
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
