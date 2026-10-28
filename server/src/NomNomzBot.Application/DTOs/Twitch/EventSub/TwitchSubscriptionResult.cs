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

using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Application.DTOs.Twitch.EventSub;

/// <summary>Twitch's response to a create / list of one subscription (twitch-eventsub §4.2).</summary>
public sealed record TwitchSubscriptionResult
{
    public required string TwitchSubscriptionId { get; init; }
    public required string Type { get; init; }
    public required string Version { get; init; }

    /// <summary><c>enabled</c> | <c>webhook_callback_verification_pending</c> | …</summary>
    public required string Status { get; init; }
    public required int Cost { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }

    /// <summary>
    /// The condition Twitch holds for this subscription — with the type and version, the key a create 409s on.
    /// Present on a listing; a create result carries none (the caller already knows what it posted).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Condition { get; init; }
}

/// <summary>The session/conduit handle a transport returns from <c>StartAsync</c> (twitch-eventsub §4.2).</summary>
public sealed record EventSubTransportHandle
{
    public required EventSubTransportKind Kind { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }
}
