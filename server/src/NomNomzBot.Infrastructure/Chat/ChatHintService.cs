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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Works out chat hints from the stored history in a few queries for the whole batch: one for the chatters' earliest
/// message ever, one per calendar day and per stream the batch touches, and one for the account dates. A message is
/// "first" when no stored message of that chatter in the window is earlier than it. The window's earliest time may
/// come from the message itself (history) or from nothing (a live line that is not stored yet), so both read right.
/// A millisecond of slack absorbs the database rounding a stored time to microseconds.
/// </summary>
public sealed class ChatHintService(IApplicationDbContext db) : IChatHintService
{
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(1);

    private readonly record struct Chatter(string Provider, string UserId);

    public async Task<IReadOnlyDictionary<string, ChatHints>> ResolveAsync(
        Guid broadcasterId,
        string? timezone,
        IReadOnlyList<ChatHintRequest> messages,
        CancellationToken ct = default
    )
    {
        if (messages.Count == 0)
            return new Dictionary<string, ChatHints>();

        TimeZoneInfo zone = ZoneOf(timezone);
        List<string> userIds = [.. messages.Select(m => m.UserId).Distinct()];
        IQueryable<ChatMessage> history = db
            .ChatMessages.IgnoreQueryFilters()
            .Where(m => m.BroadcasterId == broadcasterId && userIds.Contains(m.UserId));

        Dictionary<Chatter, DateTime> ever = await EarliestAsync(history, ct);
        Dictionary<DateTime, Dictionary<Chatter, DateTime>> byDay = await EarliestPerDayAsync(
            history,
            messages,
            zone,
            ct
        );
        Dictionary<DateTime, Dictionary<Chatter, DateTime>> byStream = await EarliestPerStreamAsync(
            history,
            messages,
            ct
        );
        Dictionary<string, DateTime?> accounts = await AccountDatesAsync(messages, ct);

        Dictionary<string, ChatHints> hints = new(messages.Count);
        foreach (ChatHintRequest message in messages)
        {
            Chatter who = new(message.Provider, message.UserId);
            DateTime day = LocalDay(message.SentAtUtc, zone);
            hints[message.MessageId] = new(
                IsFirst(ever, who, message.SentAtUtc),
                IsFirst(byDay[day], who, message.SentAtUtc),
                message.StreamStartUtc is { } stream
                    && IsFirst(byStream[stream], who, message.SentAtUtc),
                message.Provider == "twitch"
                && accounts.TryGetValue(message.UserId, out DateTime? c)
                    ? c
                    : null
            );
        }

        return hints;
    }

    private static bool IsFirst(
        Dictionary<Chatter, DateTime> earliest,
        Chatter who,
        DateTime sentAtUtc
    ) => !earliest.TryGetValue(who, out DateTime first) || sentAtUtc <= first + Slack;

    private static async Task<Dictionary<Chatter, DateTime>> EarliestAsync(
        IQueryable<ChatMessage> window,
        CancellationToken ct
    ) =>
        (
            await window
                .GroupBy(m => new { m.Provider, m.UserId })
                .Select(g => new
                {
                    g.Key.Provider,
                    g.Key.UserId,
                    First = g.Min(m => m.CreatedAt),
                })
                .ToListAsync(ct)
        ).ToDictionary(r => new Chatter(r.Provider, r.UserId), r => r.First);

    private static async Task<
        Dictionary<DateTime, Dictionary<Chatter, DateTime>>
    > EarliestPerDayAsync(
        IQueryable<ChatMessage> history,
        IReadOnlyList<ChatHintRequest> messages,
        TimeZoneInfo zone,
        CancellationToken ct
    )
    {
        Dictionary<DateTime, Dictionary<Chatter, DateTime>> byDay = [];
        foreach (DateTime day in messages.Select(m => LocalDay(m.SentAtUtc, zone)).Distinct())
        {
            DateTime start = TimeZoneInfo.ConvertTimeToUtc(day, zone);
            DateTime end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1), zone);
            byDay[day] = await EarliestAsync(
                history.Where(m => m.CreatedAt >= start && m.CreatedAt < end),
                ct
            );
        }

        return byDay;
    }

    private static async Task<
        Dictionary<DateTime, Dictionary<Chatter, DateTime>>
    > EarliestPerStreamAsync(
        IQueryable<ChatMessage> history,
        IReadOnlyList<ChatHintRequest> messages,
        CancellationToken ct
    )
    {
        Dictionary<DateTime, Dictionary<Chatter, DateTime>> byStream = [];
        foreach (
            DateTime start in messages.Select(m => m.StreamStartUtc).OfType<DateTime>().Distinct()
        )
            byStream[start] = await EarliestAsync(history.Where(m => m.CreatedAt >= start), ct);

        return byStream;
    }

    private async Task<Dictionary<string, DateTime?>> AccountDatesAsync(
        IReadOnlyList<ChatHintRequest> messages,
        CancellationToken ct
    )
    {
        List<string> twitchIds =
        [
            .. messages.Where(m => m.Provider == "twitch").Select(m => m.UserId).Distinct(),
        ];
        if (twitchIds.Count == 0)
            return [];

        return (
            await db
                .Users.IgnoreQueryFilters()
                .Where(u => u.TwitchUserId != null && twitchIds.Contains(u.TwitchUserId))
                .Select(u => new { u.TwitchUserId, u.AccountCreatedAt })
                .ToListAsync(ct)
        )
            .GroupBy(u => u.TwitchUserId!)
            .ToDictionary(g => g.Key, g => g.First().AccountCreatedAt);
    }

    /// <summary>The calendar date (midnight, unspecified kind) a UTC moment falls on in the channel's timezone.</summary>
    private static DateTime LocalDay(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date;

    private static TimeZoneInfo ZoneOf(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
