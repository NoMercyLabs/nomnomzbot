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

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Reads the moderation queue (<c>ModerationQueueItem</c>) for a row that holds a viewer's text back. The
/// redemption event and the AutoMod hold event are separate EventSub events and either can arrive first, so
/// when no row exists yet the check polls for a bounded time before it decides the text was never held.
/// Completed no-row decisions are shared by output handlers within the same event scope.
/// </summary>
public sealed partial class HeldTextService : IHeldTextService
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<(
        Guid BroadcasterId,
        string TwitchUserId,
        string Text,
        DateTimeOffset OccurredAt
    )> _completedNoHolds = [];

    public HeldTextService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<bool> IsTextHeldAsync(
        Guid broadcasterId,
        string twitchUserId,
        string text,
        CancellationToken ct = default,
        DateTimeOffset? occurredAt = null
    )
    {
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        DateTimeOffset matchAt = occurredAt ?? startedAt;
        DateTime from = (matchAt - MatchWindow).UtcDateTime;
        DateTime to = (matchAt + MatchWindow).UtcDateTime;
        string wanted = Normalize(text);

        if (occurredAt is { } eventAt)
        {
            lock (_completedNoHolds)
            {
                if (_completedNoHolds.Contains((broadcasterId, twitchUserId, wanted, eventAt)))
                    return false;
            }
        }

        while (true)
        {
            List<ModerationQueueStatus> matches = await MatchesAsync(
                broadcasterId,
                twitchUserId,
                wanted,
                from,
                to,
                ct
            );

            if (matches.Count > 0)
                return matches.Any(status => status != ModerationQueueStatus.Approved);

            if (occurredAt is not null && startedAt - matchAt > MatchWindow)
            {
                RememberNoHold(broadcasterId, twitchUserId, wanted, occurredAt);
                return false;
            }

            if (_timeProvider.GetUtcNow() - startedAt >= MaxWait)
            {
                RememberNoHold(broadcasterId, twitchUserId, wanted, occurredAt);
                return false;
            }

            await Task.Delay(PollInterval, _timeProvider, ct);
        }
    }

    public async Task<bool> IsChatLineHeldAsync(
        Guid broadcasterId,
        string twitchUserId,
        string text,
        DateTimeOffset occurredAt,
        CancellationToken ct = default
    )
    {
        List<ModerationQueueStatus> matches = await MatchesAsync(
            broadcasterId,
            twitchUserId,
            Normalize(text),
            (occurredAt - MatchWindow).UtcDateTime,
            (occurredAt + MatchWindow).UtcDateTime,
            ct
        );
        return matches.Any(status => status != ModerationQueueStatus.Approved);
    }

    private async Task<List<ModerationQueueStatus>> MatchesAsync(
        Guid broadcasterId,
        string twitchUserId,
        string wanted,
        DateTime from,
        DateTime to,
        CancellationToken ct
    ) =>
        (
            await _db
                .ModerationQueueItems.AsNoTracking()
                .Where(i =>
                    i.BroadcasterId == broadcasterId
                    && i.TargetTwitchUserId == twitchUserId
                    && i.CreatedAt >= from
                    && i.CreatedAt <= to
                )
                .Select(i => new { i.Status, i.MessageContentSnapshot })
                .ToListAsync(ct)
        )
            .Where(i => Normalize(i.MessageContentSnapshot) == wanted)
            .Select(i => i.Status)
            .ToList();

    private static string Normalize(string? text) =>
        WhitespaceRun().Replace(text ?? string.Empty, " ").Trim();

    private void RememberNoHold(
        Guid broadcasterId,
        string twitchUserId,
        string text,
        DateTimeOffset? occurredAt
    )
    {
        if (occurredAt is not { } eventAt)
            return;

        lock (_completedNoHolds)
            _completedNoHolds.Add((broadcasterId, twitchUserId, text, eventAt));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}

internal static class HeldJournalEventText
{
    public static async Task<bool> IsHeldAsync(
        IHeldTextService heldText,
        EventRecord committed,
        CancellationToken ct
    )
    {
        string? textKey = committed.EventType switch
        {
            "ChatMessageReceivedEvent"
            or "CheerEvent"
            or "ResubscriptionEvent"
            or "AutomaticRewardRedeemedEvent" => "Message",
            "RewardRedeemedEvent" or "CustomPowerUpRedeemedEvent" => "UserInput",
            "BitsUsedEvent" => "MessageText",
            "AutoModMessageHeldEvent"
            or "ChatUserMessageHeldEvent"
            or "ChatUserMessageUpdatedEvent" => "Text",
            _ => null,
        };
        if (textKey is null || committed.BroadcasterId is not { } broadcasterId)
            return false;
        if (committed.PayloadIsEncrypted)
            return true;

        try
        {
            JObject payload = JObject.Parse(committed.PayloadJson);
            string? userId = payload
                .GetValue("UserId", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            string? text = payload
                .GetValue(textKey, StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(text))
                return false;

            DateTimeOffset occurredAt = new(
                DateTime.SpecifyKind(committed.OccurredAt, DateTimeKind.Utc)
            );
            return committed.EventType == "ChatMessageReceivedEvent"
                ? await heldText.IsChatLineHeldAsync(broadcasterId, userId, text, occurredAt, ct)
                : await heldText.IsTextHeldAsync(broadcasterId, userId, text, ct, occurredAt);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
