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

using System.Globalization;
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
            "NOT_FOUND" when !string.IsNullOrWhiteSpace(data?.TrackName) => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NoCloseMatch,
                "Couldn't find \"{query}\". The closest was \"{track.name}\" by {track.artist}, so I didn't add it.",
                new Dictionary<string, string>
                {
                    ["user"] = context.UserDisplayName,
                    ["query"] = query,
                    ["track.name"] = data.TrackName,
                    ["track.artist"] = data.Artist ?? string.Empty,
                },
                ct
            ),
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
                    ? "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of {track.max}, your point has been refunded."
                    : "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of {track.max}.",
                new Dictionary<string, string>
                {
                    ["user"] = context.UserDisplayName,
                    ["track.name"] = trackName,
                    ["track.max"] = DurationText(data?.Limit),
                },
                ct
            ),
            "TRACK_EXPLICIT" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.TrackExplicit,
                "\"{track.name}\" is explicit, and explicit songs are not allowed here.",
                trackName,
                ct
            ),
            "TRACK_NOT_EMBEDDABLE" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.NotEmbeddable,
                "\"{track.name}\" can't be played here — its uploader doesn't allow it on other sites. Try another upload of the song.",
                trackName,
                ct
            ),
            "TRACK_AGE_RESTRICTED" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.AgeRestricted,
                "\"{track.name}\" is age-restricted on YouTube, so it can't play on stream. Try another upload of the song.",
                trackName,
                ct
            ),
            "TRACK_IS_LIVE" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.LiveBroadcast,
                "Live streams and premieres can't be requested. Send a link to a regular video.",
                null,
                ct
            ),
            "TRACK_MISSING" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.TrackMissing,
                "That video doesn't exist or is private.",
                null,
                ct
            ),
            "UNREADABLE_LINK" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.UnreadableLink,
                "I couldn't read that link. Paste the full video link, or send the song name instead.",
                null,
                ct
            ),
            "TRACK_LENGTH_UNKNOWN" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.LengthUnknown,
                "I couldn't tell how long that video is, so it can't be requested. Try another upload.",
                null,
                ct
            ),
            "TRACK_NOT_MUSIC" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.NotMusic,
                "That video can't be requested. YouTube requests must be in YouTube's Music category or come from an official artist (- Topic) channel. Send a YouTube Music link, or a song name.",
                null,
                ct
            ),
            "PER_STREAM_LIMIT" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.PerStreamLimit,
                "You already requested {request.limit} song(s) this stream. That is the limit.",
                new Dictionary<string, string> { ["request.limit"] = LimitText(data) },
                ct
            ),
            "SR_DISABLED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.RequestsOff,
                "Song requests are turned off in this channel.",
                null,
                ct
            ),
            "SR_CLOSED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.QueueClosed,
                "The song queue is closed.",
                null,
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
            "NO_OVERLAY_PLAYER" => TrackReplyAsync(
                context,
                BuiltinResponseSlots.SongRequest.NoOverlayPlayer,
                "Couldn't queue \"{track.name}\" — the audio overlay is not open. Open it in OBS to play YouTube requests.",
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
            // Over the provider's budget, not down: the requester gets the wait, never an "outage".
            "PROVIDER_RATE_LIMITED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.RateLimited,
                "The music service is rate-limiting this channel — try again in about {retry.minutes} minute(s).",
                new Dictionary<string, string> { ["retry.minutes"] = RetryMinutesText(data) },
                ct
            ),
            // Hours, not minutes: the requester gets the reset time, never "try again in a moment".
            "PROVIDER_QUOTA_EXHAUSTED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.QuotaExhausted,
                "YouTube's daily request limit is used up; song requests resume after {retry.time}.",
                new Dictionary<string, string> { ["retry.time"] = RetryTimeText(data) },
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
            // A link the active provider does not own and whose title could not be read.
            "UNSUPPORTED_LINK" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.UnsupportedLink,
                "That link can't be used here. Send a song name, or a link for the music service this channel uses.",
                null,
                ct
            ),
            // A link for a music service the channel switched off (YouTube not allowed).
            "PROVIDER_NOT_ALLOWED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.SourceNotAllowed,
                "This channel does not take song requests from that music service. Send a song name instead.",
                null,
                ct
            ),
            // The channel limits that music service to a higher role than the requester holds.
            "SOURCE_ROLE_REQUIRED" => ComposeAsync(
                context,
                BuiltinResponseSlots.SongRequest.SourceNeedsRole,
                "Your role can't request songs from that music service in this channel. Send a song name instead.",
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

    /// <summary>A song length for chat: whole minutes read as "6 minutes", anything else as seconds.</summary>
    private static string DurationText(int? seconds)
    {
        if (seconds is not int total)
            return "the limit";
        if (total % 60 != 0)
            return $"{total} seconds";
        int minutes = total / 60;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    /// <summary>The reset as a UTC clock time ("07:00 UTC"): chat is worldwide, so no local zone is assumed.</summary>
    private static string RetryTimeText(MusicRequestRefusal? data) =>
        data?.RetryAt is DateTimeOffset at
            ? at.ToUniversalTime().ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : "midnight Pacific time";

    private static string RetryMinutesText(MusicRequestRefusal? data) =>
        data?.RetryMinutes?.ToString() ?? "a few";

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
