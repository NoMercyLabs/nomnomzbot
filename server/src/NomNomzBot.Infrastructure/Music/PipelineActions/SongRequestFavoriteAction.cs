// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Queues the track a viewer has requested more often than any other, read from their song-request history.
///
/// <para>
/// Written for the moderator-anniversary celebration, but kept general on purpose: the viewer is a field, so
/// the same block serves a <c>!favorite</c> command, a raid welcome, or a subscriber thank-you. It queues and
/// nothing more — it never interrupts what is playing, and it writes no chat message, because the wording
/// belongs to the operator's pipeline, not to this action.
/// </para>
///
/// <para>
/// What it hands back is an OUTCOME plus the track's name: <c>queued</c>, <c>none</c> (this viewer has never
/// requested anything) and <c>unavailable</c> (the music service refused for a reason that is not about this
/// track) are three different sentences in chat, and only the pipeline can decide how to say them.
/// </para>
///
/// Parameters:
///   user_id — whose history to read (optional, defaults to the triggering user). Supports {variable}s.
///
/// Usage example:
///   { "type": "song_request_favorite" }
/// </summary>
public sealed class SongRequestFavoriteAction : ICommandAction
{
    /// <summary>How many of the viewer's favourites to try before giving up. A blocked or already-queued
    /// top pick should fall through to their number two, but walking a heavy requester's entire history
    /// would turn one celebration into dozens of provider calls.</summary>
    private const int CandidateLimit = 10;

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Refusals that are about THIS track rather than the service — worth trying the next
    /// favourite. Anything else means the provider itself cannot take a track right now, so retrying with a
    /// different uri would just repeat the same failure.</summary>
    private static readonly string[] TrackSpecificRefusals =
    [
        "TRACK_BLOCKED",
        "DUPLICATE_TRACK",
        "UNSUPPORTED_CONTENT_TYPE",
        "NOT_FOUND",
    ];

    private readonly IApplicationDbContext _db;
    private readonly IMusicService _music;
    private readonly ILogger<SongRequestFavoriteAction> _logger;

    public string ActionType => "song_request_favorite";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_request_favorite.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "user_id",
                PipelineActionFieldKind.TwitchUser,
                Required: false,
                Description: new("pipeline.song_request_favorite.user_id.help")
            ),
        ];

    public SongRequestFavoriteAction(
        IApplicationDbContext db,
        IMusicService music,
        ILogger<SongRequestFavoriteAction> logger
    )
    {
        _db = db;
        _music = music;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string userId = ResolveParam(action.GetString("user_id"), ctx);
        if (string.IsNullOrWhiteSpace(userId))
            return ActionResult.Failure(
                "song_request_favorite needs a viewer — no 'user_id' and no triggering user"
            );

        IReadOnlyList<FavoriteTrack> favorites = await LoadFavoritesAsync(
            ctx.BroadcasterId,
            userId,
            ctx.CancellationToken
        );

        if (favorites.Count == 0)
        {
            // Not a fault: a viewer who has never requested a song is an ordinary case the pipeline has
            // its own line for. Failing here would abort the rest of the celebration.
            SetOutcome(ctx, "none", track: null);
            return ActionResult.Success("no request history for this viewer");
        }

        foreach (FavoriteTrack favorite in favorites)
        {
            Result queued = await _music.AddToQueueAsync(
                ctx.BroadcasterId.ToString(),
                favorite.Track.TrackUri,
                ctx.TriggeredByDisplayName,
                ctx.CancellationToken,
                // NOT attributed to the viewer. This is the channel playing something FOR them; recording
                // it as their own request would let each celebration inflate the count the next one reads.
                requesterUserId: null
            );

            if (queued.IsSuccess)
            {
                SetOutcome(ctx, "queued", favorite);
                return ActionResult.Success($"queued favorite: {favorite.Track.TrackName}");
            }

            if (!TrackSpecificRefusals.Contains(queued.ErrorCode))
            {
                _logger.LogWarning(
                    "Could not queue {User}'s favorite on {Tenant}: {Code} {Message}",
                    userId,
                    ctx.BroadcasterId,
                    queued.ErrorCode,
                    queued.ErrorMessage
                );
                SetOutcome(ctx, "unavailable", favorite);
                return ActionResult.Success("music service could not take the track");
            }
        }

        // Every candidate was refused on its own merits — blocked, already queued, or gone from the
        // provider. Reads to the pipeline the same as having nothing to play, because it does.
        SetOutcome(ctx, "none", favorites[0]);
        return ActionResult.Success("no playable favorite among this viewer's history");
    }

    /// <summary>
    /// The viewer's tracks, most-requested first. Grouped in memory rather than in SQL because the count
    /// lives inside the row's JSON payload — the generic <c>Record</c> store has no track column to group
    /// by, and this runs once per celebration, not per chat line.
    /// </summary>
    private async Task<IReadOnlyList<FavoriteTrack>> LoadFavoritesAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken
    )
    {
        List<string> rows = await _db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId
                && r.UserId == userId
                && r.RecordType == SongRequestHistory.RecordType
            )
            .Select(r => r.Data)
            .ToListAsync(cancellationToken);

        Dictionary<string, (SongRequestHistory Track, int Count)> byTrack = new(
            StringComparer.Ordinal
        );
        foreach (string row in rows)
        {
            SongRequestHistory? track = Deserialize(row);
            if (track is null || string.IsNullOrWhiteSpace(track.TrackUri))
                continue;

            // Last write wins on the metadata: a re-titled or re-mastered track should be announced under
            // the name it carried most recently, not the one it had years ago.
            int count = byTrack.TryGetValue(track.TrackUri, out (SongRequestHistory, int) seen)
                ? seen.Item2 + 1
                : 1;
            byTrack[track.TrackUri] = (track, count);
        }

        return
        [
            .. byTrack
                .Values.OrderByDescending(t => t.Count)
                .ThenBy(t => t.Track.TrackName, StringComparer.OrdinalIgnoreCase)
                .Take(CandidateLimit)
                .Select(t => new FavoriteTrack(t.Track, t.Count)),
        ];
    }

    private SongRequestHistory? Deserialize(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<SongRequestHistory>(data, HistoryJson);
        }
        catch (JsonException ex)
        {
            // One unreadable row must not cost a viewer their whole history.
            _logger.LogWarning(ex, "Skipping unreadable song-request history row");
            return null;
        }
    }

    /// <summary>Seeds what the operator's announcement copy reads. The track is named even when it could not
    /// be queued, so a "Spotify is having a moment" line can still say which song it meant.</summary>
    private static void SetOutcome(
        PipelineExecutionContext ctx,
        string outcome,
        FavoriteTrack? track
    )
    {
        ctx.Variables["music.favorite.outcome"] = outcome;
        ctx.Variables["music.favorite.track"] = track?.Track.TrackName ?? string.Empty;
        ctx.Variables["music.favorite.artist"] = track?.Track.Artist ?? string.Empty;
        ctx.Variables["music.favorite.count"] = (track?.Count ?? 0).ToString();
    }

    private static string ResolveParam(string? value, PipelineExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ctx.TriggeredByUserId;

        if (value.StartsWith('{') && value.EndsWith('}'))
            return ctx.Variables.GetValueOrDefault(value[1..^1], string.Empty);

        return value;
    }

    private sealed record FavoriteTrack(SongRequestHistory Track, int Count);
}
