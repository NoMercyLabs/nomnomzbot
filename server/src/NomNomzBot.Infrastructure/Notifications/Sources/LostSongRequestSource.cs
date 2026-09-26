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
using NomNomzBot.Domain.Music.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Song requests that vanished from the provider's queue before they ever played
/// (<see cref="SongRequestLostAtProviderEvent"/>, typically removed by hand in the Spotify app). The bot has
/// already dropped and refunded them; the streamer may want to queue them again. The event is read back from
/// the journal, which records every published event, over the last <see cref="Window"/>. All losses in the
/// window are one item whose key embeds the set of event ids, so a new loss surfaces again after a dismissal.
/// </summary>
public sealed class LostSongRequestSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "song-lost:";

    /// <summary>How long a lost request stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(SongRequestLostAtProviderEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<EventJournal> losses = await db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.EventType == nameof(SongRequestLostAtProviderEvent)
                && e.OccurredAt >= since
            )
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
        if (losses.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string key = SetKey(losses.Select(e => e.EventId));
        if (dismissedKeys.Contains(key))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        EventJournal newest = losses[^1];
        Dictionary<string, string> parameters = new() { ["count"] = losses.Count.ToString() };
        JObject? payload = newest.PayloadIsEncrypted ? null : ParsePayload(newest.Payload);
        string? track = payload?.Value<string>(nameof(SongRequestLostAtProviderEvent.TrackName));
        string? requester = payload?.Value<string>(
            nameof(SongRequestLostAtProviderEvent.RequestedBy)
        );
        if (track is not null && requester is not null)
        {
            parameters["trackName"] = track;
            parameters["requestedBy"] = requester;
        }

        ActionRequiredItemDto item = new(
            Id: key,
            Kind: "song_request_lost",
            Severity: "info",
            TitleKey: "attention_song_lost_title",
            MessageKey: parameters.ContainsKey("trackName")
                ? "attention_song_lost_message"
                : "attention_song_lost_unnamed_message",
            Parameters: parameters,
            DetectedAt: newest.OccurredAt,
            DeepLinkRoute: "songrequests",
            SourceUserId: null,
            SourceUserName: null,
            Count: losses.Count,
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
