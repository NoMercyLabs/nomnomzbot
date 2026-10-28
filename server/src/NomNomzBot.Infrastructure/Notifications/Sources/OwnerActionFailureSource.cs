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
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Actions a widget or script ran as the owner that started and then failed
/// (<see cref="OwnerActionFailedEvent"/>): an OBS input that stayed loud, a sound that never played. The streamer
/// would otherwise see nothing but a console line in the widget. The event is read back from the journal over the
/// last <see cref="Window"/>. Each failure is its own item, because each one is a thing that did not happen on
/// stream. An OBS action links to the OBS page, any other to the widgets page.
/// <para>
/// One exception: failures caused by OBS not being connected share ONE root cause, so they fold into a single
/// "OBS is not connected" item that counts them (<see cref="ObsNotConnectedKey"/>). The folded failures stay in
/// the journal; only the warning surface folds them. The item disappears when OBS connects again (a direct
/// session established, or a bridge leader online), and a failure after that starts a new item.
/// </para>
/// </summary>
public sealed class OwnerActionFailureSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "owner-action-failed:";
    private const string ObsNotConnectedKey = KeyPrefix + "obs-not-connected";
    private const string ObsActionPrefix = "obs_";

    // The transport codes that mean "there is no OBS to talk to", as opposed to OBS refusing one request.
    private static readonly string[] ObsNotConnectedCodes =
    [
        "OBS_NOT_CONNECTED",
        "OBS_BRIDGE_OFFLINE",
    ];

    /// <summary>How long a failure stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "owner_action_failed";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    // The connect signals are here so the notice clears over the socket the moment OBS comes back.
    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(OwnerActionFailedEvent),
        nameof(ObsConnectionEstablishedEvent),
        nameof(ObsBridgeStateChangedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<EventJournal> failures = await LoadFailuresAsync(channelId, since, cancellationToken);
        DateTime? lastConnectedAt = await LastObsConnectedAtAsync(
            channelId,
            since,
            cancellationToken
        );

        List<ActionRequiredItemDto> items = [];
        List<EventJournal> notConnected = [];
        foreach (EventJournal failure in failures)
        {
            string key = KeyPrefix + failure.EventId;
            if (dismissedKeys.Contains(key))
                continue;

            if (!IsObsNotConnected(failure))
            {
                items.Add(ToItem(key, failure));
                continue;
            }

            // A failure from before OBS connected again is history, not a standing problem.
            if (lastConnectedAt is null || failure.OccurredAt > lastConnectedAt)
                notConnected.Add(failure);
        }

        if (notConnected.Count > 0)
            items.Add(ToObsNotConnectedItem(notConnected));

        return Result.Success(items);
    }

    public async Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        if (itemId != ObsNotConnectedKey)
            return Result.Success<List<string>>([itemId]);

        // The folded item stands for every failure it counts, so dismissing it dismisses each of them.
        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<EventJournal> failures = await LoadFailuresAsync(channelId, since, cancellationToken);
        return Result.Success<List<string>>([
            .. failures.Where(IsObsNotConnected).Select(f => KeyPrefix + f.EventId),
        ]);
    }

    private Task<List<EventJournal>> LoadFailuresAsync(
        Guid channelId,
        DateTime since,
        CancellationToken cancellationToken
    ) =>
        db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.EventType == nameof(OwnerActionFailedEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

    // When OBS last became reachable: a direct session was established, or a bridge reported a leader.
    private async Task<DateTime?> LastObsConnectedAtAsync(
        Guid channelId,
        DateTime since,
        CancellationToken cancellationToken
    )
    {
        List<EventJournal> signals = await db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.OccurredAt >= since
                && (
                    e.EventType == nameof(ObsConnectionEstablishedEvent)
                    || e.EventType == nameof(ObsBridgeStateChangedEvent)
                )
            )
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        EventJournal? latest = signals.FirstOrDefault(IsConnectSignal);
        return latest?.OccurredAt;
    }

    private static bool IsConnectSignal(EventJournal signal)
    {
        if (signal.EventType == nameof(ObsConnectionEstablishedEvent))
            return true;

        JObject? payload = signal.PayloadIsEncrypted ? null : ParsePayload(signal.Payload);
        return payload?.Value<bool>(nameof(ObsBridgeStateChangedEvent.HasLeader)) == true;
    }

    private static bool IsObsNotConnected(EventJournal failure)
    {
        JObject? payload = failure.PayloadIsEncrypted ? null : ParsePayload(failure.Payload);
        string? code = payload?.Value<string>(nameof(OwnerActionFailedEvent.ErrorCode));
        return code is not null && ObsNotConnectedCodes.Contains(code);
    }

    private static ActionRequiredItemDto ToObsNotConnectedItem(List<EventJournal> failures) =>
        new(
            Id: ObsNotConnectedKey,
            Kind: "obs_not_connected",
            Severity: "warning",
            TitleKey: "attention_obs_not_connected_title",
            MessageKey: "attention_obs_not_connected_message",
            Parameters: new() { ["count"] = failures.Count.ToString() },
            DetectedAt: failures.Max(f => f.OccurredAt),
            DeepLinkRoute: "obs",
            SourceUserId: null,
            SourceUserName: null,
            Count: failures.Count,
            QueueItemIds: []
        );

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
                : "overlays",
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
