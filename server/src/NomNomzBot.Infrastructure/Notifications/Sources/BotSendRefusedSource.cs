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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Twitch refused the bot's chat message in this channel (<see cref="BotChatSendRefusedEvent"/>): the bot is
/// not a moderator there, or the channel did not grant <c>channel:bot</c>. Without this item the bot would stay
/// silent and nobody would know why. The refusals are read back from the journal over the last
/// <see cref="Window"/>. All refusals since the last <see cref="BotChatSendRecoveredEvent"/> are ONE item per
/// channel, keyed by the first of them, so a repeat refusal adds no duplicate; a later send that worked
/// (the recovery) ends it, and a refusal after that starts a new item.
/// </summary>
public sealed class BotSendRefusedSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "bot-send-refused:";

    /// <summary>How long a refusal stays in the inbox when nothing ends it first.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "bot_send_refused";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(BotChatSendRefusedEvent), nameof(BotChatSendRecoveredEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<EventJournal> events = await db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.OccurredAt >= since
                && (
                    e.EventType == nameof(BotChatSendRefusedEvent)
                    || e.EventType == nameof(BotChatSendRecoveredEvent)
                )
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        DateTime? recoveredAt = events
            .LastOrDefault(e => e.EventType == nameof(BotChatSendRecoveredEvent))
            ?.OccurredAt;
        List<EventJournal> refusals =
        [
            .. events.Where(e =>
                e.EventType == nameof(BotChatSendRefusedEvent)
                && (recoveredAt is null || e.OccurredAt > recoveredAt)
            ),
        ];
        if (refusals.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string key = $"{KeyPrefix}{channelId}:{refusals[0].OccurredAt.Ticks}";
        if (dismissedKeys.Contains(key))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string? channelName = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success<List<ActionRequiredItemDto>>([
            ToItem(key, refusals[^1], channelName ?? string.Empty),
        ]);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(string key, EventJournal latest, string channelName)
    {
        JObject? payload = latest.PayloadIsEncrypted ? null : ParsePayload(latest.Payload);
        string botName =
            payload?.Value<string>(nameof(BotChatSendRefusedEvent.BotUsername)) ?? string.Empty;

        return new ActionRequiredItemDto(
            Id: key,
            Kind: "bot_send_refused",
            Severity: "warning",
            TitleKey: "attention_bot_send_refused_title",
            MessageKey: "attention_bot_send_refused_message",
            Parameters: new() { ["botName"] = botName, ["channelName"] = channelName },
            DetectedAt: latest.OccurredAt,
            DeepLinkRoute: "people/overview",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
    }

    private static JObject? ParsePayload(string payload)
    {
        try
        {
            return JObject.Parse(payload);
        }
        catch (JsonReaderException)
        {
            return null;
        }
    }
}
