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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>!skip — skips the currently playing track (mods+).</summary>
public sealed class SkipBuiltin(IMusicService music) : IBuiltinCommand
{
    public string BuiltinKey => "skip";
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 2; // mod+

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result skipped = await music.SkipAsync(context.BroadcasterId.ToString(), ct);
        return Result.Success(
            skipped.IsSuccess
                ? "Skipped!"
                : skipped.ErrorMessage ?? "Nothing to skip or skip failed."
        );
    }
}

/// <summary>!queue — shows the current song queue (first 5 tracks).</summary>
public sealed class QueueBuiltin(IMusicService music) : IBuiltinCommand
{
    public string BuiltinKey => "queue";
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        MusicQueue queue = await music.GetQueueAsync(context.BroadcasterId.ToString(), ct);
        if (queue.Queue.Count == 0)
            return Result.Success("The queue is empty.");

        IEnumerable<string> preview = queue
            .Queue.Take(5)
            .Select((t, i) => $"{i + 1}. {t.TrackName} by {t.Artist}");
        string suffix = queue.Queue.Count > 5 ? $" (+{queue.Queue.Count - 5} more)" : string.Empty;
        return Result.Success($"Queue: {string.Join(" | ", preview)}{suffix}");
    }
}

/// <summary>!volume [0–100] — gets or sets the playback volume (mods+).</summary>
public sealed class VolumeBuiltin(IMusicService music) : IBuiltinCommand
{
    public string BuiltinKey => "volume";
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 2; // mod+

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(context.Args)
            || !int.TryParse(context.Args.Trim(), out int level)
        )
            return Result.Success("Usage: !volume <0-100>");

        level = Math.Clamp(level, 0, 100);
        Result volume = await music.SetVolumeAsync(context.BroadcasterId.ToString(), level, ct);
        return Result.Success(
            volume.IsSuccess
                ? $"Volume set to {level}%."
                : volume.ErrorMessage ?? "Failed to set volume."
        );
    }
}

/// <summary>!song — shows the currently playing track.</summary>
public sealed class CurrentSongBuiltin(IMusicService music) : IBuiltinCommand
{
    public string BuiltinKey => "song";
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        NowPlaying? now = await music.GetNowPlayingAsync(context.BroadcasterId.ToString(), ct);
        if (now is null || string.IsNullOrEmpty(now.TrackName))
            return Result.Success("Nothing is playing right now.");

        string status = now.IsPlaying ? "▶" : "⏸";
        return Result.Success($"{status} {now.TrackName} by {now.Artist}");
    }
}
