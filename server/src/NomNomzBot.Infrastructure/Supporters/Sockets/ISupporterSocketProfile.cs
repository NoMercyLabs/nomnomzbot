// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Supporters.Sockets;

/// <summary>
/// One provider's live-socket dialect (supporter-events.md §0 D3): how to open its stream from the
/// connection's sealed key, what keepalive it expects, and which inbound frames carry ingestable supporter
/// payloads. The runner (<see cref="SupporterSocketHostedService"/>) is provider-agnostic — everything
/// provider-specific lives here, so a new socket provider is a profile + an <c>ISupporterSource</c>, never an
/// engine change.
/// </summary>
internal interface ISupporterSocketProfile
{
    /// <summary>The supporter source this profile connects (matches <c>ISupporterSource.SourceKey</c>).</summary>
    string SourceKey { get; }

    /// <summary>The stream endpoint for a connection's decrypted key/secret.</summary>
    Uri BuildUri(string secret);

    /// <summary>
    /// The ingestable supporter payloads carried by one inbound frame — empty for anything else
    /// (keepalive echoes, unrelated event types, unparseable noise). Each returned payload feeds
    /// <c>ISupporterIngestService.IngestAsync</c> and is normalized by the provider's <c>ISupporterSource</c>.
    /// For a raw-WS profile a frame is the wire text; for a Socket.IO profile it is the received event's
    /// argument array as JSON.
    /// </summary>
    IReadOnlyList<string> TranslateFrame(string frame);
}

/// <summary>A provider speaking plain WebSocket text frames (Pally), with an optional client keepalive.</summary>
internal interface IRawWebSocketProfile : ISupporterSocketProfile
{
    /// <summary>Client-side keepalive cadence, or null when the transport needs none.</summary>
    TimeSpan? KeepaliveInterval { get; }

    /// <summary>The text frame to send on each keepalive tick (e.g. <c>ping</c>).</summary>
    string? KeepalivePayload { get; }
}

/// <summary>
/// A provider speaking Socket.IO (Streamlabs, StreamElements, Tipeee): which named events to forward, which
/// Engine.IO protocol its server runs (3 for a Socket.IO-v2 server; 4 for v3/v4), how its endpoint resolves
/// (some providers discover their socket host over HTTP), and an optional post-connect authentication emit.
/// </summary>
internal interface ISocketIoProfile : ISupporterSocketProfile
{
    int EngineIoVersion { get; }

    IReadOnlyList<string> EventNames { get; }

    /// <summary>
    /// The stream endpoint for a connection's key — async because some providers (Tipeee) discover their
    /// socket host via an HTTP call first; a static provider just wraps <c>BuildUri</c>.
    /// </summary>
    ValueTask<Uri> ResolveEndpointAsync(string secret, HttpClient http, CancellationToken ct);

    /// <summary>The post-connect authentication/room emit, or null when the token rides the query alone.</summary>
    SocketIoEmit? BuildConnectEmit(string secret);
}

/// <summary>A named Socket.IO emit with its payload object (serialized by the client).</summary>
internal sealed record SocketIoEmit(string EventName, object Payload);
