// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal static class PredictionText
{
    private const string Active = "ACTIVE";
    private const string Locked = "LOCKED";
    private const int OddsPlaces = 2;

    public static bool IsUnfinished(TwitchPrediction prediction) =>
        prediction.Status is Active or Locked;

    public static bool IsOpen(TwitchPrediction prediction) => prediction.Status == Active;

    /// <summary>The status in the words a viewer uses: Twitch's <c>ACTIVE</c> reads <c>open</c>.</summary>
    public static string StatusWord(TwitchPrediction prediction) =>
        prediction.Status switch
        {
            Active => "open",
            _ => prediction.Status.ToLowerInvariant(),
        };

    public static long TotalPoints(TwitchPrediction prediction) =>
        prediction.Outcomes.Sum(o => (long)o.ChannelPoints);

    public static long TotalUsers(TwitchPrediction prediction) =>
        prediction.Outcomes.Sum(o => (long)o.Users);

    /// <summary>The outcome with the most points (the first on a tie), or null while nobody has bet.</summary>
    public static TwitchPredictionOutcome? Leading(TwitchPrediction prediction) =>
        prediction.Outcomes.Count == 0 || TopPoints(prediction) == 0
            ? null
            : prediction.Outcomes.First(o => o.ChannelPoints == TopPoints(prediction));

    public static string Share(TwitchPredictionOutcome outcome, TwitchPrediction prediction) =>
        PollText.Percent(outcome.ChannelPoints, TotalPoints(prediction));

    public static string Points(long points, CultureInfo culture) =>
        StreamText.Grouped(points, culture);

    public static string Titles(TwitchPrediction prediction) =>
        string.Join(", ", prediction.Outcomes.Select(o => o.Title));

    /// <summary>
    /// What one point on the outcome pays, as a ratio of the pot: <c>1:4</c> when the outcome holds a quarter of all
    /// points. An outcome nobody bet on has no ratio.
    /// </summary>
    public static string Odds(TwitchPredictionOutcome outcome, TwitchPrediction prediction) =>
        outcome.ChannelPoints == 0
            ? string.Empty
            : "1:"
                + NumberText.Plain(
                    NumberText.Round(
                        (decimal)TotalPoints(prediction) / outcome.ChannelPoints,
                        OddsPlaces
                    )
                );

    /// <summary>The player who put the most points on any outcome: <c>ViewerOne (1,000)</c>.</summary>
    public static string TopPredictor(TwitchPrediction prediction, CultureInfo culture) =>
        prediction
            .Outcomes.SelectMany(o => o.TopPredictors ?? [])
            .OrderByDescending(p => p.ChannelPointsUsed)
            .FirstOrDefault()
            is { } top
            ? $"{top.UserName} ({Points(top.ChannelPointsUsed, culture)})"
            : string.Empty;

    /// <summary>How long bets stay open; empty once the prediction is locked.</summary>
    public static string TimeLeft(TwitchPrediction prediction, DateTimeOffset now)
    {
        if (!IsOpen(prediction))
            return string.Empty;

        DateTimeOffset locksAt = prediction.CreatedAt.AddSeconds(prediction.PredictionWindow);
        return StreamText.Short(locksAt > now ? locksAt - now : TimeSpan.Zero);
    }

    /// <summary>The outcome the argument names, <c>""</c> past the end of the list, null for a bad number.</summary>
    public static string? WithOutcome(
        PredictionCall call,
        TwitchPrediction prediction,
        Func<TwitchPredictionOutcome, TwitchPrediction, string> read
    ) =>
        !StreamText.TryPositive(call.Args[0], int.MaxValue, out int number) ? null
        : number > prediction.Outcomes.Count ? string.Empty
        : read(prediction.Outcomes[number - 1], prediction);

    private static int TopPoints(TwitchPrediction prediction) =>
        prediction.Outcomes.Count == 0 ? 0 : prediction.Outcomes.Max(o => o.ChannelPoints);
}
