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

    /// <summary>Client-side keepalive cadence, or null when the transport needs none.</summary>
    TimeSpan? KeepaliveInterval { get; }

    /// <summary>The text frame to send on each keepalive tick (e.g. <c>ping</c>).</summary>
    string? KeepalivePayload { get; }

    /// <summary>
    /// The ingestable supporter payloads carried by one inbound text frame — empty for anything else
    /// (keepalive echoes, unrelated event types, unparseable noise). Each returned payload feeds
    /// <c>ISupporterIngestService.IngestAsync</c> and is normalized by the provider's <c>ISupporterSource</c>.
    /// </summary>
    IReadOnlyList<string> TranslateFrame(string frame);
}
