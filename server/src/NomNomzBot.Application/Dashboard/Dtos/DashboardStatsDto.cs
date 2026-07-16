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

namespace NomNomzBot.Application.Dashboard.Dtos;

/// <summary>
/// Snapshot of a channel's current state, returned by GET /api/v1/dashboard/{broadcasterId}/stats.
/// Live fields come from the in-memory ChannelContext; counts are session totals since bot join.
/// </summary>
public sealed record DashboardStatsDto
{
    [JsonPropertyName("isLive")]
    public bool IsLive { get; init; }

    [JsonPropertyName("streamTitle")]
    public string? StreamTitle { get; init; }

    [JsonPropertyName("gameName")]
    public string? GameName { get; init; }

    /// <summary>Last known viewer count (0 if offline or not yet received from Twitch).</summary>
    [JsonPropertyName("viewerCount")]
    public int ViewerCount { get; init; }

    /// <summary>Real Twitch follower total (Get Channel Followers); 0 when the Helix read fails.</summary>
    [JsonPropertyName("followerCount")]
    public int FollowerCount { get; init; }

    /// <summary>Real Twitch subscriber total (Get Broadcaster Subscriptions); 0 when the Helix read fails.</summary>
    [JsonPropertyName("subscriberCount")]
    public int SubscriberCount { get; init; }

    /// <summary>Distinct chatters seen today (UTC) — the privacy-hashed ChannelChatterDays count.</summary>
    [JsonPropertyName("chattersToday")]
    public int ChattersToday { get; init; }

    /// <summary>Supporter events (tips/memberships/merch/charity) received today (UTC).</summary>
    [JsonPropertyName("supporterEventsToday")]
    public int SupporterEventsToday { get; init; }

    /// <summary>
    /// Today's supporter total in minor units — only when every amount-bearing event today shares ONE
    /// currency (<see cref="SupporterCurrency"/>); a mixed-currency day reports null rather than a
    /// meaningless cross-currency sum.
    /// </summary>
    [JsonPropertyName("supporterAmountMinorToday")]
    public long? SupporterAmountMinorToday { get; init; }

    /// <summary>The single currency behind <see cref="SupporterAmountMinorToday"/>, else null.</summary>
    [JsonPropertyName("supporterCurrency")]
    public string? SupporterCurrency { get; init; }

    /// <summary>Commands successfully executed this session.</summary>
    [JsonPropertyName("commandsUsed")]
    public long CommandsUsed { get; init; }

    /// <summary>Chat messages received this session.</summary>
    [JsonPropertyName("messagesCount")]
    public long MessagesCount { get; init; }

    /// <summary>Stream uptime in whole seconds, null if offline.</summary>
    [JsonPropertyName("uptime")]
    public long? Uptime { get; init; }
}
