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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>
/// The channel's own YouTube numbers, read from the YouTube Analytics API with the streamer's grant
/// (scope set <c>youtube.analytics</c>).
/// </summary>
public interface IYouTubeAnalyticsService
{
    /// <summary>
    /// One row per day in the range. A channel with no YouTube connection, or one that did not allow
    /// analytics, is a normal state: the answer says so in <see cref="YouTubeAnalyticsDto.Access"/> and holds
    /// no days. A channel outside the YouTube Partner Program gets its numbers with no revenue.
    /// </summary>
    Task<Result<YouTubeAnalyticsDto>> GetDailyAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );
}

/// <summary>One day of the channel's YouTube activity. The revenue is null when YouTube gives none.</summary>
public sealed record YouTubeAnalyticsDayDto(
    DateOnly Day,
    long Views,
    long MinutesWatched,
    long SubscribersGained,
    long SubscribersLost,
    decimal? EstimatedRevenue
);

/// <summary>
/// Whether the bot may read the channel's YouTube analytics. Text values, so the dashboard reads the word and
/// not a number.
/// </summary>
public static class YouTubeAnalyticsAccess
{
    /// <summary>The numbers were read.</summary>
    public const string Allowed = "allowed";

    /// <summary>The channel has no YouTube connection.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>The channel is connected, and the streamer did not allow analytics yet.</summary>
    public const string NotAllowed = "not_allowed";
}

/// <summary>
/// The channel's YouTube activity per day, and whether YouTube reports revenue for it.
/// <paramref name="Access"/> is one of <see cref="YouTubeAnalyticsAccess"/>.
/// </summary>
public sealed record YouTubeAnalyticsDto(
    string Access,
    IReadOnlyList<YouTubeAnalyticsDayDto> Days,
    bool RevenueAvailable
);
