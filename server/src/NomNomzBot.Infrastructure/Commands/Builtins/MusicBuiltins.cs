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

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !skip — two distinct actions sharing one trigger word (S-PL8, owner report). Bare <c>!skip</c> (no
/// argument) skips whatever is currently PLAYING — a moderation action, gated mod+, unchanged from
/// before. <c>!skip N</c> is a VIEWER self-service action: it removes the caller's own Nth PENDING
/// request from the queue and never touches playback — before this fix <c>!skip N</c> silently ignored
/// N and skipped the current track regardless of who typed it or what N was.
/// </summary>
public sealed class SkipBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Skip.Key;
    public int DefaultCooldownSeconds => 5;

    // Everyone may TYPE !skip — the mod+ floor for the no-argument (skip-current) branch is enforced
    // inside ExecuteAsync, because the N-argument branch must stay open to plain viewers (self-service
    // removal of their own queued request). Gating the whole builtin at the door (as before) would have
    // blocked a viewer from ever reaching the N branch.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string args = context.Args.Trim();
        return args.Length == 0
            ? await SkipCurrentTrackAsync(context, ct)
            : await RemoveOwnQueuedRequestAsync(context, args, ct);
    }

    /// <summary>Bare <c>!skip</c> — the pre-existing mod+ "skip what's playing now" behavior.</summary>
    private async Task<Result<string>> SkipCurrentTrackAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        // Moderator on the UNIFIED ladder (0/2/4/6/10/…) — the old value 2 was Subscriber, silently
        // letting any sub skip tracks while the comment claimed mod+ (found in the item-24c audit).
        // This check used to live in DefaultMinPermissionLevel; it moved here when the N-argument
        // branch opened the builtin to Everyone (see DefaultMinPermissionLevel above).
        if (context.RoleLevel < PermissionLevel.Moderator.ToLevelValue())
            // Same wording ChatMessageHandler's own permission-denied notice uses — stays neutral,
            // never personality.
            return Result.Success("You don't have permission to use that command.");

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

    /// <summary>
    /// <c>!skip N</c> — removes the CALLING viewer's own Nth pending request from the queue. N counts
    /// only the caller's own entries (their 1st, 2nd, … own queued request, in queue order) — never the
    /// global queue position, so a viewer can never name and remove someone else's request. Mirrors the
    /// "my own request only" ownership check <c>SongWrongAction</c> (<c>!wrongsong</c>) already uses:
    /// matching <see cref="MusicQueueItem.RequestedBy"/> against the caller's display name. Never calls
    /// <see cref="IMusicService.SkipAsync"/> — the currently-playing track is never touched by this path.
    /// </summary>
    private async Task<Result<string>> RemoveOwnQueuedRequestAsync(
        BuiltinCommandContext context,
        string args,
        CancellationToken ct
    )
    {
        if (!int.TryParse(args, out int n) || n < 1)
            return Result.Success(
                "Usage: !skip <N> — removes YOUR Nth queued request. !skip with no number skips the current track (mods+)."
            );

        string broadcasterId = context.BroadcasterId.ToString();
        MusicQueue queue = await music.GetQueueAsync(broadcasterId, ct);

        int ownMatchesSeen = 0;
        int position = -1;
        MusicQueueItem? item = null;
        for (int i = 0; i < queue.Queue.Count; i++)
        {
            if (!IsCallers(queue.Queue[i], context))
                continue;

            ownMatchesSeen++;
            if (ownMatchesSeen != n)
                continue;

            position = i;
            item = queue.Queue[i];
            break;
        }

        if (item is null)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} You don't have a request at position {n}."
            );

        bool removed = await music.RemoveFromQueueAsync(broadcasterId, position, ct);
        if (!removed)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} Couldn't remove that request — try again."
            );

        return Result.Success(
            $"@{context.TriggeringUserDisplayName} Removed your request: {item.TrackName} by {item.Artist}"
        );
    }

    private static bool IsCallers(MusicQueueItem queued, BuiltinCommandContext context) =>
        string.Equals(
            queued.RequestedBy,
            context.TriggeringUserDisplayName,
            StringComparison.OrdinalIgnoreCase
        );
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
            if (nowPlaying is not null)
                return Result.Success($"Volume is at {nowPlaying.Volume}%.");

            string cannotRead = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Volume.Key,
                    Slot = BuiltinResponseSlots.Volume.CannotRead,
                    NeutralFallback =
                        "Can't read the current volume right now — nothing is playing.",
                },
                ct
            );
            return Result.Success(cannotRead);
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
