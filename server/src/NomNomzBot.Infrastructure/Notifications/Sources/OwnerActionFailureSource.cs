// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Actions a widget or script ran as the owner that started and then failed
/// (<see cref="OwnerActionFailedEvent"/>): an OBS input that stayed loud, a sound that never played. The streamer
/// would otherwise see nothing but a console line in the widget. The event is read back from the journal over the
/// last <see cref="Window"/>. Each failure is its own item, because each one is a thing that did not happen on
/// stream. An OBS action links to the OBS page, any other to the widgets page.
/// </summary>
public sealed class OwnerActionFailureSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "owner-action-failed:";
    private const string ObsActionPrefix = "obs_";

    /// <summary>How long a failure stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(OwnerActionFailedEvent)];

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
                && e.EventType == nameof(OwnerActionFailedEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [];
        foreach (EventJournal failure in failures)
        {
            string key = KeyPrefix + failure.EventId;
            if (dismissedKeys.Contains(key))
                continue;
            items.Add(ToItem(key, failure));
        }

        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(string key, EventJournal failure)
    {
        JObject? payload = failure.PayloadIsEncrypted ? null : ParsePayload(failure.Payload);
        string actionType =
            payload?.Value<string>(nameof(OwnerActionFailedEvent.ActionType)) ?? string.Empty;
        string error = payload?.Value<string>(nameof(OwnerActionFailedEvent.Error)) ?? string.Empty;

        Dictionary<string, string> parameters = new()
        {
            ["action"] = actionType,
            ["error"] = error,
        };

        return new ActionRequiredItemDto(
            Id: key,
            Kind: "owner_action_failed",
            Severity: "warning",
            TitleKey: "attention_owner_action_failed_title",
            MessageKey: "attention_owner_action_failed_message",
            Parameters: parameters,
            DetectedAt: failure.OccurredAt,
            DeepLinkRoute: actionType.StartsWith(ObsActionPrefix, StringComparison.Ordinal)
                ? "obs"
                : "widgets",
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
