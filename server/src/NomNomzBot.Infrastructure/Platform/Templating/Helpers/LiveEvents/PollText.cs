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

using System.Globalization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal static class PollText
{
    private const string Separator = ", ";
    private const int HalfMinute = 30;

    public static int TotalVotes(TwitchPoll poll) => poll.Choices.Sum(c => c.Votes);

    /// <summary>The option with the most votes (the first on a tie), or null while nobody has voted.</summary>
    public static TwitchPollChoice? Leading(TwitchPoll poll) =>
        poll.Choices.Count == 0 || TopVotes(poll) == 0
            ? null
            : poll.Choices.First(c => c.Votes == TopVotes(poll));

    public static bool IsTie(TwitchPoll poll) =>
        TopVotes(poll) > 0 && poll.Choices.Count(c => c.Votes == TopVotes(poll)) > 1;

    /// <summary>A share of the whole as text, rounded to a whole percent: <c>60%</c>. Nothing counted reads <c>0%</c>.</summary>
    public static string Percent(long part, long whole) =>
        Math.Round(whole == 0 ? 0m : part * 100m / whole, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Share(TwitchPollChoice choice, TwitchPoll poll) =>
        Percent(choice.Votes, TotalVotes(poll));

    public static string Titles(TwitchPoll poll) =>
        string.Join(Separator, poll.Choices.Select(c => c.Title));

    public static string Numbered(TwitchPoll poll) =>
        string.Join(" ", poll.Choices.Select((c, i) => $"{i + 1}. {c.Title}"));

    public static string Bars(TwitchPoll poll) =>
        string.Join(
            " · ",
            poll.Choices.Select(c =>
                $"{c.Title} {MeterBars.Draw(ShareValue(c, poll), MeterBars.DefaultWidth, MeterBars.Full, MeterBars.Empty)} {Share(c, poll)}"
            )
        );

    public static DateTimeOffset EndsAt(TwitchPoll poll) =>
        poll.StartedAt.AddSeconds(poll.Duration);

    /// <summary>The end as a wall-clock time in the channel zone, rounded to the nearest minute (15:31:30 reads <c>15:32</c>).</summary>
    public static string EndsAtLocal(TwitchPoll poll, string? zoneId) =>
        StreamText.LocalTime(EndsAt(poll).AddSeconds(HalfMinute), zoneId);

    public static string TimeLeft(TwitchPoll poll, DateTimeOffset now) =>
        StreamText.Short(EndsAt(poll) > now ? EndsAt(poll) - now : TimeSpan.Zero);

    /// <summary>The option the argument names, <c>""</c> past the end of the list, null for a bad number.</summary>
    public static string? WithOption(
        PollCall call,
        TwitchPoll poll,
        Func<TwitchPollChoice, TwitchPoll, string> read
    ) =>
        !StreamText.TryPositive(call.Args[0], int.MaxValue, out int number) ? null
        : number > poll.Choices.Count ? string.Empty
        : read(poll.Choices[number - 1], poll);

    private static int TopVotes(TwitchPoll poll) =>
        poll.Choices.Count == 0 ? 0 : poll.Choices.Max(c => c.Votes);

    private static decimal ShareValue(TwitchPollChoice choice, TwitchPoll poll) =>
        TotalVotes(poll) == 0 ? 0m : choice.Votes * 100m / TotalVotes(poll);
}
