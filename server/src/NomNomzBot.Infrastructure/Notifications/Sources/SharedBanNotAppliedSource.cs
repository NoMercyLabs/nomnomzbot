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
/// Shared bans this channel should have placed but did not (<see cref="SharedChatBanNotAppliedEvent"/>): Twitch
/// refused the ban, the channel was not in the same shared-chat session, or the origin is not on its trust list.
/// The channel opted in to the trust web, so the streamer is told why a viewer was NOT banned. The event is read
/// back from the journal over the last <see cref="Window"/>. Failures of one reason are one item whose key embeds
/// the set of event ids, so a new failure surfaces again after a dismissal.
/// </summary>
public sealed class SharedBanNotAppliedSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "shared-ban:";

    /// <summary>How long a failure stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "shared_bans";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(SharedChatBanNotAppliedEvent)];

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
                && e.EventType == nameof(SharedChatBanNotAppliedEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [];
        foreach (
            IGrouping<string, (EventJournal Row, JObject? Payload)> group in failures
                .Select(row =>
                    (Row: row, Payload: row.PayloadIsEncrypted ? null : ParsePayload(row.Payload))
                )
                .Where(f => ReasonOf(f.Payload) is not null)
                .GroupBy(f => ReasonOf(f.Payload)!)
        )
        {
            string key = SetKey(group.Key, group.Select(f => f.Row.EventId));
            if (dismissedKeys.Contains(key))
                continue;

            items.Add(ToItem(key, group.Key, group.ToList()));
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
        string reason,
        List<(EventJournal Row, JObject? Payload)> failures
    )
    {
        (EventJournal newest, JObject? payload) = failures[^1];
        Dictionary<string, string> parameters = new()
        {
            ["count"] = failures.Count.ToString(),
            ["targetName"] =
                Text(payload, nameof(SharedChatBanNotAppliedEvent.TargetDisplayName))
                ?? Text(payload, nameof(SharedChatBanNotAppliedEvent.TargetTwitchUserId))
                ?? "",
        };
        string? detail = Text(payload, nameof(SharedChatBanNotAppliedEvent.Detail));
        if (detail is not null)
            parameters["detail"] = detail;

        return new(
            Id: key,
            Kind: "shared_ban_not_applied",
            Severity: reason == SharedBanNotAppliedReasons.OriginNotTrusted ? "info" : "warning",
            TitleKey: "attention_shared_ban_title",
            MessageKey: $"attention_shared_ban_{reason}_message",
            Parameters: parameters,
            DetectedAt: newest.OccurredAt,
            DeepLinkRoute: "moderation",
            SourceUserId: null,
            SourceUserName: null,
            Count: failures.Count,
            QueueItemIds: []
        );
    }

    private static string? ReasonOf(JObject? payload) =>
        Text(payload, nameof(SharedChatBanNotAppliedEvent.Reason));

    private static string? Text(JObject? payload, string property)
    {
        string? value = payload?.Value<string>(property);
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

    private static string SetKey(string reason, IEnumerable<Guid> eventIds)
    {
        string joined = string.Join(',', eventIds.OrderBy(id => id));
        return $"{KeyPrefix}{reason}:"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }
}
