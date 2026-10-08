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
/// Messages a moderation rule wanted to delete but the platform refused
/// (<see cref="AutoModDeleteFailedEvent"/>, for example the bot is not a moderator). Those messages are still on
/// screen. The event is read back from the journal over the last <see cref="Window"/>. All failures in the
/// window are one item whose key embeds the set of event ids, so a new failure surfaces again after a dismissal.
/// </summary>
public sealed class AutoModDeleteFailedSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "automod-delete-failed:";

    /// <summary>How long a failure stays in the inbox, long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(AutoModDeleteFailedEvent)];

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
                && e.EventType == nameof(AutoModDeleteFailedEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
        if (failures.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string key = SetKey(failures.Select(e => e.EventId));
        if (dismissedKeys.Contains(key))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        EventJournal newest = failures[^1];
        JObject? payload = newest.PayloadIsEncrypted ? null : ParsePayload(newest.Payload);
        Dictionary<string, string> parameters = new()
        {
            ["count"] = failures.Count.ToString(),
            ["ruleName"] = payload?.Value<string>(nameof(AutoModDeleteFailedEvent.RuleName)) ?? "",
            ["userName"] = payload?.Value<string>(nameof(AutoModDeleteFailedEvent.UserLogin)) ?? "",
        };

        ActionRequiredItemDto item = new(
            Id: key,
            Kind: "automod_delete_failed",
            Severity: "warning",
            TitleKey: "attention_automod_delete_failed_title",
            MessageKey: "attention_automod_delete_failed_message",
            Parameters: parameters,
            DetectedAt: newest.OccurredAt,
            DeepLinkRoute: "moderation",
            SourceUserId: null,
            SourceUserName: null,
            Count: failures.Count,
            QueueItemIds: []
        );
        return Result.Success<List<ActionRequiredItemDto>>([item]);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

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

    private static string SetKey(IEnumerable<Guid> eventIds)
    {
        string joined = string.Join(',', eventIds.OrderBy(id => id));
        return KeyPrefix
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }
}
