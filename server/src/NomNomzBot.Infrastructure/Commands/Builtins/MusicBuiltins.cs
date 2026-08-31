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

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>!skip — skips the currently playing track (mods+). Confirms in the channel's tone.</summary>
public sealed class SkipBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Skip.Key;
    public int DefaultCooldownSeconds => 5;

    // Moderator on the UNIFIED ladder (0/2/4/6/10/…) — the old value 2 was Subscriber, silently
    // letting any sub skip tracks while the comment claimed mod+ (found in the item-24c audit).
    public int DefaultMinPermissionLevel => 10; // mod+

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result skipped = await music.SkipAsync(context.BroadcasterId.ToString(), ct);
        if (!skipped.IsSuccess)
            // Functional error — stays neutral, never personality.
            return Result.Success(skipped.ErrorMessage ?? "Nothing to skip or skip failed.");

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.Skip.Skipped,
                NeutralFallback = "Skipped.",
            },
            ct
        );
        return Result.Success(message);
    }
}

/// <summary>!queue — shows the current song queue (first 5 tracks) in the channel's tone.</summary>
public sealed class QueueBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Queue.Key;
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        MusicQueue queue = await music.GetQueueAsync(context.BroadcasterId.ToString(), ct);
        if (queue.Queue.Count == 0)
        {
            string empty = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = BuiltinResponseSlots.Queue.Empty,
                    NeutralFallback = "The queue is empty.",
                },
                ct
            );
            return Result.Success(empty);
        }

        IEnumerable<string> preview = queue
            .Queue.Take(5)
            .Select((t, i) => $"{i + 1}. {t.TrackName} by {t.Artist}");
        string more = queue.Queue.Count > 5 ? $"+{queue.Queue.Count - 5} more" : string.Empty;
        string list = string.Join(" | ", preview) + (more.Length > 0 ? $" ({more})" : string.Empty);
        MusicQueueItem first = queue.Queue[0];

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.Queue.List,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "Queue: {queue.list}",
                Variables = new Dictionary<string, string>
                {
                    ["queue.count"] = queue.Queue.Count.ToString(),
                    ["queue.list"] = list,
                    ["queue.next"] = $"{first.TrackName} by {first.Artist}",
                    ["queue.more"] = more,
                },
            },
            ct
        );
        return Result.Success(message);
    }
}

/// <summary>
/// !volume [0–100] — gets or sets the playback volume (mods+). The set path stays neutral (a plain numeric
/// confirmation); the missing/unparsable-argument usage message is tone-styled (S069h).
/// </summary>
public sealed class VolumeBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => "volume";
    public int DefaultCooldownSeconds => 5;

    // Moderator on the UNIFIED ladder — see SkipBuiltin; 2 was Subscriber, not mod.
    public int DefaultMinPermissionLevel => 10; // mod+

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(context.Args))
        {
            // No argument — report the current volume instead of the old "do nothing but print
            // usage" behaviour. Read from the same place the dashboard reads it from (NowPlaying),
            // the only volume accessor the provider surface exposes; when that can't be read (no
            // active track / provider doesn't report it) say so truthfully — never guess a number.
            NowPlaying? nowPlaying = await music.GetNowPlayingAsync(
                context.BroadcasterId.ToString(),
                ct
            );
            return Result.Success(
                nowPlaying is not null
                    ? $"Volume is at {nowPlaying.Volume}%."
                    : "Can't read the current volume right now — nothing is playing."
            );
        }

        if (!int.TryParse(context.Args.Trim(), out int level))
        {
            string usage = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Volume.Key,
                    Slot = BuiltinResponseSlots.Volume.Usage,
                    NeutralFallback = "Usage: !volume <0-100>",
                },
                ct
            );
            return Result.Success(usage);
        }

        level = Math.Clamp(level, 0, 100);
        Result volume = await music.SetVolumeAsync(context.BroadcasterId.ToString(), level, ct);
        return Result.Success(
            volume.IsSuccess
                ? $"Volume set to {level}%."
                : volume.ErrorMessage ?? "Failed to set volume."
        );
    }
}

/// <summary>!song — shows the currently playing track in the channel's tone.</summary>
public sealed class CurrentSongBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Song.Key;
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        NowPlaying? now = await music.GetNowPlayingAsync(context.BroadcasterId.ToString(), ct);
        if (now is null || string.IsNullOrEmpty(now.TrackName))
        {
            string nothing = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = BuiltinResponseSlots.Song.Nothing,
                    NeutralFallback = "Nothing is playing right now.",
                },
                ct
            );
            return Result.Success(nothing);
        }

        string status = now.IsPlaying ? "▶" : "⏸";

        // A track with no requester was NOT requested by anyone — it is the provider's own playback
        // (Spotify autoplay, a playlist rolling on, YouTube's next video). Saying "requested by someone"
        // there invents a viewer, and it is the difference between "the queue is working" and "the queue
        // is empty and Spotify took over" — which chat and the streamer both need to be able to tell.
        bool wasRequested = !string.IsNullOrWhiteSpace(now.RequestedBy);
        string source = wasRequested ? "request" : "autoplay";
        string provider = string.IsNullOrWhiteSpace(now.Provider) ? "the playlist" : now.Provider;
        string attribution = wasRequested
            ? $"(requested by {now.RequestedBy})"
            : $"(from {provider})";

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.Song.Playing,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "{song.status} {song.name} by {song.artist} {song.attribution}",
                Variables = new Dictionary<string, string>
                {
                    ["song.name"] = now.TrackName,
                    ["song.artist"] = now.Artist ?? string.Empty,
                    ["song.status"] = status,
                    // Empty when nobody requested it, so a custom template can branch on it.
                    ["song.requester"] = now.RequestedBy ?? string.Empty,
                    ["song.source"] = source,
                    ["song.provider"] = provider,
                    ["song.attribution"] = attribution,
                },
            },
            ct
        );
        return Result.Success(message);
    }
}
