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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !sr &lt;query&gt; — requests a song to be added to the queue. Delegates to IMusicService for search and
/// queue management. Every reply — success, usage and each refusal — is its own slot the streamer can
/// re-word; refusals are phrased from the service's typed error code plus its structured data, never from
/// the service's own sentence.
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
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.SongRequest.Usage,
                    "Usage: !sr <song name or URL>",
                    null,
                    ct
                )
            );

        // One resolve: a pasted track link lands on its exact track, a search phrase falls through to the
        // provider's search — then straight into the fair queue (music-sr.md §3.9).
        Result<MusicTrack> requested = await _music.RequestTrackAsync(
            context.BroadcasterId.ToString(),
            query,
            context.TriggeringUserDisplayName,
            context.RoleLevel,
            ct,
            // The requester's platform id, so this request lands in THEIR history. The display name above is
            // only the fair-queue owner key and cannot own a history that outlives a rename.
            context.TriggeringUserId
        );

        if (requested.IsFailure)
            return Result.Success(
                await new SongRequestRefusalReplies(_composer).RefusalReplyAsync(
                    new(
                        context.BroadcasterId,
                        context.Personality,
                        context.TriggeringUserDisplayName,
                        context.RoleLevel,
                        IsReward: false
                    ),
                    query,
                    requested,
                    ct
                )
            );

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
                    UserDisplayName = context.TriggeringUserDisplayName,
                    UserLogin = context.TriggeringUserLogin,
                },
                ct
            );
        }

        string message = await ComposeAsync(
            context,
            BuiltinResponseSlots.SongRequest.Added,
            "Added {track.name} by {track.artist} to the queue. {track.link}",
            new Dictionary<string, string>
            {
                ["user"] = context.TriggeringUserDisplayName,
                ["track.name"] = track.Name,
                ["track.artist"] = track.Artist,
                ["track.link"] = trackLink,
            },
            ct
        );
        return Result.Success(message);
    }

    private Task<string> ComposeAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        _composer.ComposeAsync(
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

    /// <summary>
    /// A provider-agnostic, directly-clickable web URL for the track — YouTube's <see cref="MusicTrack.Uri"/>
    /// is already a real <c>https://</c> watch URL, but Spotify's is the internal <c>spotify:track:&lt;id&gt;</c>
    /// URI scheme, which chat clients and this bot's own OG-preview resolver can't fetch metadata for. Falls
    /// back to the raw URI for any other/future provider that already hands back a real link.
    /// </summary>
    private static string TrackWebLink(MusicTrack track) => TrackLinks.ToWebLink(track.Uri);
}
