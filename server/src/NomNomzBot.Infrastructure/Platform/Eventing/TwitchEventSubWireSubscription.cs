// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The Helix <c>data[]</c> element shape for <c>POST/GET /eventsub/subscriptions</c>. The shared Helix
/// transport deserializes with System.Text.Json Web defaults (case-insensitive, no snake_case policy), so
/// the snake_case multi-word fields carry explicit <see cref="JsonPropertyNameAttribute"/>s. Mapped to the
/// app-facing <c>TwitchSubscriptionResult</c> by the transport.
/// </summary>
public sealed class TwitchEventSubWireSubscription
{
    public string? Id { get; init; }
    public string? Status { get; init; }
    public string? Type { get; init; }
    public string? Version { get; init; }
    public int? Cost { get; init; }
    public TwitchEventSubWireTransport? Transport { get; init; }
}

/// <summary>The <c>transport</c> object inside a Helix EventSub subscription row.</summary>
public sealed class TwitchEventSubWireTransport
{
    public string? Method { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("conduit_id")]
    public string? ConduitId { get; init; }

    [JsonPropertyName("callback")]
    public string? CallbackUrl { get; init; }
}
