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

/// <summary>
/// !skip — two distinct actions sharing one trigger word (S-PL8, owner report). Bare <c>!skip</c> (no
/// argument) skips whatever is currently PLAYING — a moderator (or a holder of the music moderate grant)
/// skips any track; any other viewer skips only the track they requested, like the old bot. <c>!skip N</c> is a VIEWER self-service action: it removes the caller's own Nth PENDING
/// request from the queue and never touches playback — before this fix <c>!skip N</c> silently ignored
/// N and skipped the current track regardless of who typed it or what N was.
/// </summary>
public sealed class SkipBuiltin(
    IMusicService music,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
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
        // Moderator badge OR the music:queue:moderate grant — the same Gate-2 action the dashboard's
        // POST /music/skip requires, so one grant works in both places (MusicModerationGate).
        bool moderates = await gate.IsAllowedAsync(context, ct);
        if (!moderates)
        {
            // Like the old bot: anyone may type !skip, but a viewer skips only the song they requested.
            string? refusal = await RefuseViewerSkipAsync(context, ct);
            if (refusal is not null)
                return Result.Success(refusal);
        }

        Result skipped = await music.SkipAsync(
            context.BroadcasterId.ToString(),
            context.TriggeringUserId,
            context.TriggeringPlatform,
            ct
        );
        if (!skipped.IsSuccess)
            return Result.Success(await SkipFailureReplyAsync(context, skipped, ct));

        string message = moderates
            ? await composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.Skipped,
                "Skipped to the next track.",
                ct: ct
            )
            : await composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.SkippedOwn,
                "Skipped your song.",
                ct: ct
            );
        return Result.Success(message);
    }

    /// <summary>The reply that stops a non-moderator's skip (nothing playing, or not their request); null when the track is theirs.</summary>
    private async Task<string?> RefuseViewerSkipAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        NowPlaying? now = await music.GetNowPlayingAsync(context.BroadcasterId.ToString(), ct);
        if (now is null || string.IsNullOrEmpty(now.TrackName))
            return await composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.NothingPlaying,
                "No song is currently playing!",
                ct: ct
            );

        if (IsCaller(now.RequestedBy, context))
            return null;

        return await composer.ComposeAsync(
            context,
            BuiltinKey,
            BuiltinResponseSlots.Skip.NotYours,
            "You can only skip songs you requested yourself.",
            ct: ct
        );
    }

    private static bool IsCaller(string? requestedBy, BuiltinCommandContext context) =>
        !string.IsNullOrWhiteSpace(requestedBy)
        && (
            string.Equals(
                requestedBy,
                context.TriggeringUserDisplayName,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                requestedBy,
                context.TriggeringUserLogin,
                StringComparison.OrdinalIgnoreCase
            )
        );

    /// <summary>Phrases a failed skip from the service's typed error code — the service's own sentence stays in logs.</summary>
    private Task<string> SkipFailureReplyAsync(
        BuiltinCommandContext context,
        Result skipped,
        CancellationToken ct
    ) =>
        skipped.ErrorCode switch
        {
            "SERVICE_UNAVAILABLE" => composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.NoProvider,
                "No active music provider.",
                ct: ct
            ),
            "PREMIUM_REQUIRED" => composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.PremiumRequired,
                "The music service needs a Premium account to skip.",
                ct: ct
            ),
            _ => composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.Failed,
                "Nothing to skip or skip failed.",
                ct: ct
            ),
        };

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
                await composer.ComposeAsync(
                    context,
                    BuiltinKey,
                    BuiltinResponseSlots.Skip.Usage,
                    "Usage: !skip <N> — removes YOUR Nth queued request. !skip with no number skips the current track (mods+).",
                    ct: ct
                )
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
                await composer.ComposeAsync(
                    context,
                    BuiltinKey,
                    BuiltinResponseSlots.Skip.NoRequest,
                    "@{user} You don't have a request at position {request.position}.",
                    new Dictionary<string, string>
                    {
                        ["user"] = context.TriggeringUserDisplayName,
                        ["request.position"] = n.ToString(),
                    },
                    ct
                )
            );

        bool removed = await music.RemoveFromQueueAsync(broadcasterId, position, ct);
        if (!removed)
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinKey,
                    BuiltinResponseSlots.Skip.RemoveFailed,
                    "@{user} Couldn't remove that request — try again.",
                    new Dictionary<string, string> { ["user"] = context.TriggeringUserDisplayName },
                    ct
                )
            );

        return Result.Success(
            await composer.ComposeAsync(
                context,
                BuiltinKey,
                BuiltinResponseSlots.Skip.Removed,
                "@{user} Removed your request: {track.name} by {track.artist}",
                new Dictionary<string, string>
                {
                    ["user"] = context.TriggeringUserDisplayName,
                    ["track.name"] = item.TrackName,
                    ["track.artist"] = item.Artist,
                },
                ct
            )
        );
    }

    private static bool IsCallers(MusicQueueItem queued, BuiltinCommandContext context) =>
        string.Equals(
            queued.RequestedBy,
            context.TriggeringUserDisplayName,
            StringComparison.OrdinalIgnoreCase
        );
}

/// <summary>
/// !queue [@user] — the song queue in the channel's tone. <c>!queue @user</c> answers with that viewer's
/// own requests (queue position and a rough wait for each); a plain <c>!queue</c> does the same for a caller
/// who has requests queued, and otherwise lists the first five tracks with who asked for each.
/// </summary>
public sealed class QueueBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    private const int PreviewSize = 5;

    public string BuiltinKey => BuiltinResponseSlots.Queue.Key;
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        MusicQueue queue = await music.GetQueueAsync(context.BroadcasterId.ToString(), ct);
        string? target = TargetName(context.Args);

        if (queue.Queue.Count == 0 && target is null)
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinKey,
                    BuiltinResponseSlots.Queue.Empty,
                    "The queue is empty.",
                    ct: ct
                )
            );

        List<int> positions = RequestPositions(queue.Queue, target, context);
        if (positions.Count > 0)
            return Result.Success(await OwnRequestsAsync(context, queue, positions, ct));

        if (target is not null)
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinKey,
                    BuiltinResponseSlots.Queue.None,
                    "{user} has no songs in the queue.",
                    new Dictionary<string, string> { ["user"] = target },
                    ct
                )
            );

        return Result.Success(await ListAsync(context, queue.Queue, ct));
    }

    private Task<string> ListAsync(
        BuiltinCommandContext context,
        IReadOnlyList<MusicQueueItem> items,
        CancellationToken ct
    )
    {
        IEnumerable<string> preview = items
            .Take(PreviewSize)
            .Select((item, i) => $"{i + 1}. {Describe(item)}{RequesterSuffix(item)}");
        string more =
            items.Count > PreviewSize ? $"+{items.Count - PreviewSize} more" : string.Empty;
        string list = string.Join(" | ", preview) + (more.Length > 0 ? $" ({more})" : string.Empty);

        return composer.ComposeAsync(
            context,
            BuiltinKey,
            BuiltinResponseSlots.Queue.List,
            "Queue: {queue.list}",
            new Dictionary<string, string>
            {
                ["queue.count"] = items.Count.ToString(),
                ["queue.list"] = list,
                ["queue.next"] = Describe(items[0]),
                ["queue.more"] = more,
            },
            ct
        );
    }

    private Task<string> OwnRequestsAsync(
        BuiltinCommandContext context,
        MusicQueue queue,
        List<int> positions,
        CancellationToken ct
    )
    {
        IEnumerable<string> mine = positions.Select(position =>
            $"#{position + 1} {Describe(queue.Queue[position])} ({FormatWait(WaitBefore(queue, position))})"
        );

        return composer.ComposeAsync(
            context,
            BuiltinKey,
            BuiltinResponseSlots.Queue.Mine,
            "{user}: {queue.mine}",
            new Dictionary<string, string>
            {
                ["user"] =
                    queue.Queue[positions[0]].RequestedBy ?? context.TriggeringUserDisplayName,
                ["queue.mine"] = string.Join(" | ", mine),
                ["queue.mine.count"] = positions.Count.ToString(),
            },
            ct
        );
    }

    // "!queue @f0xb17 extra words" → "f0xb17"; no argument → null (the caller asks about themselves).
    private static string? TargetName(string args)
    {
        string first = args.Trim().Split(' ', 2)[0].TrimStart('@');
        return first.Length == 0 ? null : first;
    }

    // Queue indexes of the subject's requests. The subject is the named viewer, or the caller; requests are
    // matched on the requester's name, the same ownership rule !skip N and !wrongsong use.
    private static List<int> RequestPositions(
        IReadOnlyList<MusicQueueItem> items,
        string? target,
        BuiltinCommandContext context
    )
    {
        List<int> positions = [];
        for (int i = 0; i < items.Count; i++)
        {
            bool matches = target is null
                ? IsNamed(items[i], context.TriggeringUserDisplayName)
                    || IsNamed(items[i], context.TriggeringUserLogin)
                : IsNamed(items[i], target);
            if (matches)
                positions.Add(i);
        }
        return positions;
    }

    private static bool IsNamed(MusicQueueItem item, string name) =>
        name.Length > 0
        && string.Equals(item.RequestedBy, name, StringComparison.OrdinalIgnoreCase);

    // What still has to play before the track at [position] starts: the rest of the current track plus
    // every queued track ahead of it.
    private static TimeSpan WaitBefore(MusicQueue queue, int position)
    {
        NowPlaying? current = queue.CurrentTrack;
        int remainingMs = current is { IsPlaying: true }
            ? Math.Max(0, current.DurationMs - current.ProgressMs)
            : 0;
        long aheadMs = queue.Queue.Take(position).Sum(item => (long)item.DurationMs);
        return TimeSpan.FromMilliseconds(remainingMs + aheadMs);
    }

    private static string FormatWait(TimeSpan wait) =>
        wait < TimeSpan.FromMinutes(1)
            ? "up next"
            : $"in ~{(int)Math.Ceiling(wait.TotalMinutes)} min";

    private static string Describe(MusicQueueItem item) => $"{item.TrackName} by {item.Artist}";

    private static string RequesterSuffix(MusicQueueItem item) =>
        string.IsNullOrWhiteSpace(item.RequestedBy) ? string.Empty : $" ({item.RequestedBy})";
}

/// <summary>
/// !volume [0–100] — gets or sets the playback volume (mods+). The set path stays neutral (a plain numeric
/// confirmation); the missing/unparsable-argument usage message is tone-styled (S069h).
/// </summary>
public sealed class VolumeBuiltin(
    IMusicService music,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public string BuiltinKey => "volume";
    public int DefaultCooldownSeconds => 5;

    // Everyone may TYPE !volume: the moderator-or-music:queue:moderate-grant check runs inside
    // ExecuteAsync (MusicModerationGate), so a non-mod chatter holding the grant is not stopped at the
    // door by the role-ladder floor.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (!await gate.IsAllowedAsync(context, ct))
            return Result.Success(await composer.ComposePermissionDeniedAsync(context, ct));

        // Like the old bot, nothing playing answers the same way for a read and for a set. The volume is
        // read from the same place the dashboard reads it (NowPlaying), the only volume accessor the
        // provider surface exposes — never a guessed number.
        NowPlaying? nowPlaying = await music.GetNowPlayingAsync(
            context.BroadcasterId.ToString(),
            ct
        );
        if (nowPlaying is null)
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinResponseSlots.Volume.Key,
                    BuiltinResponseSlots.Volume.CannotRead,
                    "No song is currently playing!",
                    ct: ct
                )
            );

        if (string.IsNullOrWhiteSpace(context.Args))
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinResponseSlots.Volume.Key,
                    BuiltinResponseSlots.Volume.Current,
                    "Current volume level is {volume.level}",
                    new Dictionary<string, string>
                    {
                        ["volume.level"] = nowPlaying.Volume.ToString(),
                    },
                    ct
                )
            );

        // An out-of-range or non-numeric level is rejected, never clamped (the old bot did the same).
        if (!int.TryParse(context.Args.Trim(), out int level) || level is < 0 or > 100)
            return Result.Success(
                await composer.ComposeAsync(
                    context,
                    BuiltinResponseSlots.Volume.Key,
                    BuiltinResponseSlots.Volume.Usage,
                    "Please provide a valid volume level between 0 and 100: !volume <level> (0-100).",
                    ct: ct
                )
            );

        Result volume = await music.SetVolumeAsync(context.BroadcasterId.ToString(), level, ct);
        return Result.Success(
            volume.IsSuccess
                ? await composer.ComposeAsync(
                    context,
                    BuiltinResponseSlots.Volume.Key,
                    BuiltinResponseSlots.Volume.Set,
                    "Volume set to {volume.level}%.",
                    new Dictionary<string, string> { ["volume.level"] = level.ToString() },
                    ct
                )
                : await VolumeFailureReplyAsync(context, volume, ct)
        );
    }

    /// <summary>Phrases a failed volume change from the service's typed error code — the service's own sentence stays in logs.</summary>
    private Task<string> VolumeFailureReplyAsync(
        BuiltinCommandContext context,
        Result failure,
        CancellationToken ct
    ) =>
        failure.ErrorCode switch
        {
            "SERVICE_UNAVAILABLE" => composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Volume.Key,
                BuiltinResponseSlots.Volume.NoProvider,
                "No active music provider.",
                ct: ct
            ),
            "PREMIUM_REQUIRED" => composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Volume.Key,
                BuiltinResponseSlots.Volume.PremiumRequired,
                "The music service needs a Premium account to change the volume.",
                ct: ct
            ),
            _ => composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Volume.Key,
                BuiltinResponseSlots.Volume.SetFailed,
                "Failed to set volume.",
                ct: ct
            ),
        };
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
                context,
                BuiltinKey,
                BuiltinResponseSlots.Song.Nothing,
                "No song is currently playing!",
                ct: ct
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
                NeutralFallback =
                    "The current song is: {song.name} by {song.artist} {song.link} {song.attribution}",
                Variables = new Dictionary<string, string>
                {
                    ["song.name"] = now.TrackName,
                    ["song.link"] = TrackLinks.ToWebLink(now.TrackUri),
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
        // A track with no known link leaves {song.link} empty; keep the sentence free of a dangling space.
        return Result.Success(message.Trim());
    }
}
