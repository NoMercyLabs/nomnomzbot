// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}

/// <summary>The session/conduit handle a transport returns from <c>StartAsync</c> (twitch-eventsub §4.2).</summary>
public sealed record EventSubTransportHandle
{
    public required EventSubTransportKind Kind { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }
}
