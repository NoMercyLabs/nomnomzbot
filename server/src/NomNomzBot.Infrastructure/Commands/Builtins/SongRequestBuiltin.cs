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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !sr &lt;query&gt; — requests a song to be added to the queue. Delegates to IMusicService for search and
/// queue management, then phrases the added / not-found outcome in the channel's personality tone. Pure
/// usage and "could not add" errors stay neutral (functional).
/// </summary>
public sealed class SongRequestBuiltin : IBuiltinCommand
{
    private readonly IMusicService _music;
    private readonly IBuiltinResponseComposer _composer;
    private readonly IEventBus _events;

    public SongRequestBuiltin(
        IMusicService music,
        IBuiltinResponseComposer composer,
        IEventBus events
    )
    {
        _music = music;
        _composer = composer;
        _events = events;
    }

    public string BuiltinKey => BuiltinResponseSlots.SongRequest.Key;
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string query = context.Args.Trim();
        if (string.IsNullOrWhiteSpace(query))
            // Pure usage string — functional, never personality. Sent as a reply, so no "@user" prefix.
            return Result.Success("Usage: !sr <song name or URL>");

        // One resolve: a pasted track link lands on its exact track, a search phrase falls through to the
        // provider's search — then straight into the fair queue (music-sr.md §3.9).
        Result<MusicTrack> requested = await _music.RequestTrackAsync(
            context.BroadcasterId.ToString(),
            query,
            context.TriggeringUserDisplayName,
            context.RoleLevel,
            ct
        );

        if (requested.IsFailure)
        {
            if (requested.ErrorCode == "NOT_FOUND")
            {
                string notFound = await _composer.ComposeAsync(
                    new()
                    {
                        BroadcasterId = context.BroadcasterId,
                        Personality = context.Personality,
                        BuiltinKey = BuiltinKey,
                        Slot = BuiltinResponseSlots.SongRequest.NotFound,
                        NeutralFallback = "No tracks found for \"{query}\".",
                        Variables = new Dictionary<string, string>
                        {
                            ["user"] = context.TriggeringUserDisplayName,
                            ["query"] = query,
                        },
                    },
                    ct
                );
                return Result.Success(notFound);
            }

            // A duplicate is the ONE refusal that is about the channel's vibe rather than a fault, and
            // it is the one viewers trigger most — so it speaks in the channel's chosen tone (sassy gets
            // to be sassy) instead of a flat sentence. The original requester is named so chat can see
            // someone genuinely got there first and it is not the bot glitching.
            if (requested.ErrorCode == "DUPLICATE_TRACK")
            {
                string duplicate = await _composer.ComposeAsync(
                    new()
                    {
                        BroadcasterId = context.BroadcasterId,
                        Personality = context.Personality,
                        BuiltinKey = BuiltinKey,
                        Slot = BuiltinResponseSlots.SongRequest.Duplicate,
                        NeutralFallback = requested.ErrorMessage!,
                        // ErrorDetail is the original requester, set structurally by MusicService — the
                        // track title is NOT available here (the resolve failed), so the toned templates
                        // deliberately speak without it rather than parsing it back out of the sentence.
                        Variables = new Dictionary<string, string>
                        {
                            ["user"] = context.TriggeringUserDisplayName,
                            ["requested.by"] = string.IsNullOrWhiteSpace(requested.ErrorDetail)
                                ? "someone"
                                : requested.ErrorDetail,
                        },
                    },
                    ct
                );
                return Result.Success(duplicate);
            }

            // Functional failures — stay neutral. Sent as a reply, so no "@user" prefix. Each refusal
            // reason gets its own honest wording rather than one blanket "could not add": a blocked
            // track carries its typed reason straight through, and anything else (a genuinely erroring
            // provider — auth broken, API down) degrades to the same "try again" wording rather than a
            // confusing internal error code.
            if (requested.ErrorCode == "SERVICE_UNAVAILABLE")
                return Result.Success(await NoProviderMessageAsync(context, ct));

            return Result.Success(
                requested.ErrorCode switch
                {
                    "SR_DISABLED" => requested.ErrorMessage!,
                    "MIN_TRUST_LEVEL" => requested.ErrorMessage!,
                    "TRACK_BLOCKED" => requested.ErrorMessage!,
                    "DUPLICATE_TRACK" => requested.ErrorMessage!,
                    "NO_ACTIVE_DEVICE" => requested.ErrorMessage!,
                    "PREMIUM_REQUIRED" => requested.ErrorMessage!,
                    "MUSIC_AUTH_FAILED" => requested.ErrorMessage!,
                    "MUSIC_FORBIDDEN" => requested.ErrorMessage!,
                    // Admission-gate refusals (MusicService.EnqueueResolvedAsync) — the requester is
                    // over a real, configured limit, not facing an outage. Must never fall through to
                    // the generic "couldn't reach the music service" wording below (S-OWN12).
                    "QUEUE_FULL" => requested.ErrorMessage!,
                    "PER_USER_LIMIT" => requested.ErrorMessage!,
                    // The search/resolve itself never meaningfully ran (dead token/not connected, or a
                    // live provider outage) — this must never be worded as "nothing matched", which would
                    // claim the search ran cleanly and the song simply doesn't exist.
                    "MISSING_SCOPE" => requested.ErrorMessage!,
                    "PROVIDER_UNAVAILABLE" => requested.ErrorMessage!,
                    // A real playlist/album/episode/show/artist link — never a search miss, so it must
                    // never render as "No tracks found for <url>".
                    "UNSUPPORTED_CONTENT_TYPE" => requested.ErrorMessage!,
                    _ =>
                        $"Couldn't reach the music service for \"{query}\" — try again in a moment.",
                }
            );
        }

        MusicTrack track = requested.Value;

        // A real, clickable web link (not the provider's internal URI scheme) so chat's own link-preview
        // resolution — the same OG-preview pipeline any pasted link already gets — turns this confirmation
        // into a real preview card (art, title, artist) instead of plain text.
        string trackLink = TrackWebLink(track);

        // Replace the caller's own "!sr <url-or-query>" line in the overlay with the track's card. A QUERY
        // carries no link at all, so the link-preview step can never help it — but the provider just told us
        // the name, artist and artwork, which is better data than scraping OpenGraph would have produced
        // anyway. Fire-and-forget by design: an overlay that misses this still shows the original line, and a
        // failure here must never fail the song request itself.
        if (!string.IsNullOrEmpty(context.MessageId))
        {
            await _events.PublishAsync(
                new ChatMessageEnrichedEvent
                {
                    BroadcasterId = context.BroadcasterId,
                    MessageId = context.MessageId,
                    LinkUrl = trackLink,
                    Title = track.Name,
                    Description = track.Artist,
                    ImageUrl = track.ImageUrl,
                    Provider = track.Provider,
                },
                ct
            );
        }

        string message = await _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.SongRequest.Added,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "Added {track.name} by {track.artist} to the queue. {track.link}",
                Variables = new Dictionary<string, string>
                {
                    ["user"] = context.TriggeringUserDisplayName,
                    ["track.name"] = track.Name,
                    ["track.artist"] = track.Artist,
                    ["track.link"] = trackLink,
                },
            },
            ct
        );
        return Result.Success(message);
    }

    /// <summary>
    /// A provider-agnostic, directly-clickable web URL for the track — YouTube's <see cref="MusicTrack.Uri"/>
    /// is already a real <c>https://</c> watch URL, but Spotify's is the internal <c>spotify:track:&lt;id&gt;</c>
    /// URI scheme, which chat clients and this bot's own OG-preview resolver can't fetch metadata for. Falls
    /// back to the raw URI for any other/future provider that already hands back a real link.
    /// </summary>
    private static string TrackWebLink(MusicTrack track)
    {
        const string spotifyUriPrefix = "spotify:track:";
        return track.Uri.StartsWith(spotifyUriPrefix, StringComparison.Ordinal)
            ? $"https://open.spotify.com/track/{track.Uri[spotifyUriPrefix.Length..]}"
            : track.Uri;
    }

    /// <summary>
    /// "No active music provider" is a different problem for a different person: only the broadcaster
    /// can authorize a Spotify/YouTube connection (dashboard OAuth), so a viewer or mod telling them to
    /// "connect Spotify" is telling them to do something they cannot do. The broadcaster gets the
    /// actionable instruction; a mod gets told to flag it upward. A viewer gets no internal detail at
    /// all — to them the command simply reads as disabled, same as any other command they don't have
    /// the reward/config for (that viewer-facing line is tone-styled, S069i).
    /// </summary>
    private async Task<string> NoProviderMessageAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        if (context.RoleLevel >= PermissionLevel.Broadcaster.ToLevelValue())
            return "Song requests aren't connected yet — connect Spotify or YouTube in the dashboard.";

        if (context.RoleLevel >= PermissionLevel.Moderator.ToLevelValue())
            return "Song requests aren't connected — let the broadcaster know to connect Spotify or YouTube in the dashboard.";

        return await _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.SongRequest.Key,
                Slot = BuiltinResponseSlots.SongRequestErrors.Disabled,
                NeutralFallback = "This command is currently disabled.",
            },
            ct
        );
    }
}
