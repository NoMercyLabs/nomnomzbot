// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>Who a song request reply is for: shared by the <c>!sr</c> builtin and the reward/pipeline action.</summary>
internal sealed record SongRequestReplyContext(
    Guid BroadcasterId,
    string Personality,
    string UserDisplayName,
    int RoleLevel,
    bool IsReward
);

/// <summary>
/// Phrases every refused song request from the tone catalogue, so <c>!sr</c> and the song reward answer alike.
/// </summary>
internal sealed class SongRequestRefusalReplies(IBuiltinResponseComposer composer)
{
    private const string BuiltinKey = BuiltinResponseSlots.SongRequest.Key;

    /// <summary>
    /// Phrases a refused request from its typed error code plus the structured
    /// <see cref="MusicRequestRefusal"/> — every refusal is its own re-wordable reply slot. The service's
    /// <see cref="Result.ErrorMessage"/> stays a log/API sentence and never reaches chat.
    /// </summary>
    public Task<string> RefusalReplyAsync(
        SongRequestReplyContext context,
        string query,
        Result<MusicTrack> refusal,
        CancellationToken ct
    )
    {
        MusicRequestRefusal? data = refusal.ErrorData as MusicRequestRefusal;
        string trackName = string.IsNullOrWhiteSpace(data?.TrackName) ? query : data.TrackName;

        return refusal.ErrorCode switch
        {
            "NOT_FOUND" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NotFound,
                "No tracks found for \"{query}\".",
                new Dictionary<string, string>
                {
                    ["user"] = context.UserDisplayName,
                    ["query"] = query,
                },
                ct
            ),
            "DUPLICATE_TRACK" => DuplicateReplyAsync(context, trackName, data, ct),
            "SERVICE_UNAVAILABLE" => NoProviderReplyAsync(context, ct),
            "TRACK_TOO_LONG" => ComposeAsync(
                context,
                context.IsReward
                    ? BuiltinResponseSlots.SongRequest.TrackTooLongRefunded
                    : BuiltinResponseSlots.SongRequest.TrackTooLong,
                context.IsReward
                    ? "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of 10 minutes, your point has been refunded."
                    : "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of 10 minutes.",
                new Dictionary<string, string>
                {
                    ["user"] = context.UserDisplayName,
                    ["track.name"] = trackName,
                },
                ct
            ),
            "SR_DISABLED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.RequestsOff,
                "Song requests are turned off in this channel.",
                null,
                ct
            ),
            "MIN_TRUST_LEVEL" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.MinTrust,
                "Song requests need at least {trust.level} right now.",
                new Dictionary<string, string>
                {
                    ["trust.level"] = data?.TrustLevel ?? "a higher role",
                },
                ct
            ),
            "SR_REVOKED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.Revoked,
                "Stop requesting songs, your permission has been revoked",
                null,
                ct
            ),
            "TRACK_BLOCKED" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.TrackBlocked,
                "\"{track.name}\" is blocked in this channel.",
                trackName,
                ct
            ),
            "NO_ACTIVE_DEVICE" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.NoActiveDevice,
                "Couldn't queue \"{track.name}\" — nothing is playing on any device right now. Start playback and try again.",
                trackName,
                ct
            ),
            "PREMIUM_REQUIRED" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.PremiumRequired,
                "Couldn't queue \"{track.name}\" — a Premium account is required for that.",
                trackName,
                ct
            ),
            "MUSIC_AUTH_FAILED" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.AuthFailed,
                "Couldn't queue \"{track.name}\" — the music connection needs to be reconnected.",
                trackName,
                ct
            ),
            "MUSIC_FORBIDDEN" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.Forbidden,
                "Couldn't queue \"{track.name}\" — the music connection doesn't have permission for that.",
                trackName,
                ct
            ),
            // Admission-gate refusals: the requester is over a real, configured limit, not facing an outage
            // — they must never fall through to the generic "couldn't reach" wording below (S-OWN12).
            "QUEUE_FULL" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.QueueFull,
                "The queue is full ({queue.max} max) — try again once it's shorter.",
                new Dictionary<string, string> { ["queue.max"] = LimitText(data) },
                ct
            ),
            "PER_USER_LIMIT" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.PerUserLimit,
                "You already have {request.limit} request(s) queued — wait for one to play before adding more.",
                new Dictionary<string, string> { ["request.limit"] = LimitText(data) },
                ct
            ),
            // The search/resolve never meaningfully ran (dead token, or a live outage) — this must never be
            // worded as "nothing matched", which would claim the song simply doesn't exist.
            "PROVIDER_NOT_CONFIGURED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NotConfigured,
                "YouTube song requests are not set up on this bot yet. The bot owner must add a YouTube API key.",
                null,
                ct
            ),
            "MISSING_SCOPE" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.MissingScope,
                "The music connection needs to be reconnected.",
                null,
                ct
            ),
            "PROVIDER_UNAVAILABLE" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.ProviderUnavailable,
                "The music provider is temporarily unavailable.",
                null,
                ct
            ),
            // A real playlist/album/episode/show/artist link — never a search miss.
            "UNSUPPORTED_CONTENT_TYPE" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.UnsupportedContent,
                "Song requests only take individual tracks — that link is a playlist, album, episode, show, or artist page. Paste a single track link, or just search by name instead.",
                null,
                ct
            ),
            // Spotify would accept it into its queue and then skip it without a word.
            "TRACK_UNAVAILABLE" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NotPlayable,
                "That track can't play in the streamer's country. Try a different version of the song.",
                null,
                ct
            ),
            _ => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.Unreachable,
                "Couldn't reach the music service for \"{query}\" — try again in a moment.",
                new Dictionary<string, string> { ["query"] = query },
                ct
            ),
        };
    }

    /// <summary>
    /// A duplicate is the ONE refusal that is about the channel's vibe rather than a fault, and the one viewers
    /// trigger most — so it speaks in the channel's tone. A track playing right now has its own slot; a queued
    /// track names its first requester so chat can see someone genuinely got there first and it is not the
    /// bot glitching.
    /// </summary>
    private Task<string> DuplicateReplyAsync(
        SongRequestReplyContext context,
        string trackName,
        MusicRequestRefusal? data,
        CancellationToken ct
    )
    {
        Dictionary<string, string> variables = new()
        {
            ["user"] = context.UserDisplayName,
            ["track.name"] = trackName,
            ["track.artist"] = data?.Artist ?? string.Empty,
        };

        if (data?.IsPlayingNow == true)
            return ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.AlreadyPlaying,
                "\"{track.name}\" is playing right now.",
                variables,
                ct
            );

        variables["requested.by"] = string.IsNullOrWhiteSpace(data?.RequestedBy)
            ? "someone"
            : data.RequestedBy;
        return ComposeAsync(
            context,
            BuiltinResponseSlots.SongRequest.Duplicate,
            "\"{track.name}\" is already in the queue (requested by {requested.by}).",
            variables,
            ct
        );
    }

    private Task<string> TrackReplyAsync(
        SongRequestReplyContext context,
        string slot,
        string neutralFallback,
        string trackName,
        CancellationToken ct
    ) =>
        ComposeAsync(
            context,
            slot,
            neutralFallback,
            new Dictionary<string, string> { ["track.name"] = trackName },
            ct
        );

    private static string LimitText(MusicRequestRefusal? data) =>
        data?.Limit?.ToString() ?? "the limit";

    /// <summary>
    /// "No active music provider" is a different problem for a different person: only the broadcaster
    /// can authorize a Spotify/YouTube connection (dashboard OAuth), so a viewer or mod telling them to
    /// "connect Spotify" is telling them to do something they cannot do. The broadcaster gets the
    /// actionable instruction; a mod gets told to flag it upward. A viewer gets no internal detail at
    /// all — to them the command simply reads as disabled, same as any other command they don't have
    /// the reward/config for (that viewer-facing line is tone-styled, S069i).
    /// </summary>
    private Task<string> NoProviderReplyAsync(SongRequestReplyContext context, CancellationToken ct)
    {
        if (context.RoleLevel >= PermissionLevel.Broadcaster.ToLevelValue())
            return ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NoProviderBroadcaster,
                "Song requests aren't connected yet — connect Spotify or YouTube in the dashboard.",
                null,
                ct
            );

        if (context.RoleLevel >= PermissionLevel.Moderator.ToLevelValue())
            return ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NoProviderModerator,
                "Song requests aren't connected — let the broadcaster know to connect Spotify or YouTube in the dashboard.",
                null,
                ct
            );

        return ComposeAsync(
            context,
            BuiltinResponseSlots.SongRequestErrors.Disabled,
            "This command is currently disabled.",
            null,
            ct
        );
    }

    private Task<string> ComposeAsync(
        SongRequestReplyContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ct
        );
}
