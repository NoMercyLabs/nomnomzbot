// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Moderation.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Chat filter actions the platform refused (<see cref="ChatFilterActionFailedEvent"/>): the filter matched, but
/// the message or the viewer is still in chat. The event is read back from the journal over the last
/// <see cref="Window"/>. Failures of one filter and one action are one item, so a raid does not flood the inbox;
/// the item names the newest chatter and the platform's reason. Its key embeds the set of event ids, so a new
/// failure surfaces again after a dismissal.
/// </summary>
public sealed class ChatFilterActionFailureSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "chat-filter-failed:";

    /// <summary>How long a failure stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "chat_filter_action_failures";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(ChatFilterActionFailedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<EventJournal> failures = await db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.EventType == nameof(ChatFilterActionFailedEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [];
        foreach (
            IGrouping<string, (EventJournal Row, JObject Payload)> group in failures
                .Select(row =>
                    (Row: row, Payload: row.PayloadIsEncrypted ? null : ParsePayload(row.Payload))
                )
                .Where(f => f.Payload is not null)
                .Select(f => (f.Row, Payload: f.Payload!))
                .GroupBy(f =>
                    $"{Text(f.Payload, nameof(ChatFilterActionFailedEvent.FilterId))}:"
                    + Text(f.Payload, nameof(ChatFilterActionFailedEvent.Action))
                )
        )
        {
            string key = SetKey(group.Key, group.Select(f => f.Row.EventId));
            if (dismissedKeys.Contains(key))
                continue;

            items.Add(ToItem(key, group.ToList()));
        }

        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(
        string key,
        List<(EventJournal Row, JObject Payload)> failures
    )
    {
        (EventJournal newest, JObject payload) = failures[^1];
        string? twitchUserId = Text(
            payload,
            nameof(ChatFilterActionFailedEvent.SubjectTwitchUserId)
        );
        string? username = Text(payload, nameof(ChatFilterActionFailedEvent.SubjectUsername));

        Dictionary<string, string> parameters = new()
        {
            ["filter"] = Text(payload, nameof(ChatFilterActionFailedEvent.FilterName)) ?? "",
            ["username"] = username ?? twitchUserId ?? "",
            ["action"] = Text(payload, nameof(ChatFilterActionFailedEvent.Action)) ?? "",
            ["reason"] = Text(payload, nameof(ChatFilterActionFailedEvent.Error)) ?? "",
        };

        return new(
            Id: key,
            Kind: "chat_filter_action_failed",
            Severity: "warning",
            TitleKey: "attention_filter_action_failed_title",
            MessageKey: "attention_filter_action_failed_message",
            Parameters: parameters,
            DetectedAt: newest.OccurredAt,
            DeepLinkRoute: "moderation",
            SourceUserId: twitchUserId,
            SourceUserName: username,
            Count: failures.Count,
            QueueItemIds: []
        );
    }

    private static string? Text(JObject payload, string property)
    {
        string? value = payload.Value<string>(property);
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    private static string SetKey(string group, IEnumerable<Guid> eventIds)
    {
        string joined = string.Join(',', eventIds.OrderBy(id => id));
        return $"{KeyPrefix}{group}:"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }
}
