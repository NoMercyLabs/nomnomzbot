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
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Moderation.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Viewers the bot should have timed out for heat, but the platform refused the timeout
/// (<see cref="UserHeatAutoTimeoutFailedEvent"/>). Nobody was timed out, so a human has to act. The event is read
/// back from the journal over the last <see cref="Window"/>. Each failure is its own item, because each one
/// names a different viewer who still needs action.
/// </summary>
public sealed class HeatAutoTimeoutFailureSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "heat-timeout-failed:";

    /// <summary>How long a failure stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "heat_timeouts";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(UserHeatAutoTimeoutFailedEvent)];

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
                && e.EventType == nameof(UserHeatAutoTimeoutFailedEvent)
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
        string? twitchUserId = payload?.Value<string>(
            nameof(UserHeatAutoTimeoutFailedEvent.SubjectTwitchUserId)
        );
        string? username = payload?.Value<string>(
            nameof(UserHeatAutoTimeoutFailedEvent.SubjectUsername)
        );
        string? error = payload?.Value<string>(nameof(UserHeatAutoTimeoutFailedEvent.Error));

        Dictionary<string, string> parameters = new()
        {
            ["username"] = username ?? twitchUserId ?? string.Empty,
            ["error"] = error ?? string.Empty,
        };

        return new ActionRequiredItemDto(
            Id: key,
            Kind: "heat_auto_timeout_failed",
            Severity: "warning",
            TitleKey: "attention_heat_timeout_failed_title",
            MessageKey: "attention_heat_timeout_failed_message",
            Parameters: parameters,
            DetectedAt: failure.OccurredAt,
            DeepLinkRoute: "people/overview",
            SourceUserId: twitchUserId,
            SourceUserName: username,
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
