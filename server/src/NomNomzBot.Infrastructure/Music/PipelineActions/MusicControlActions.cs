// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Silent broadcaster-operator music actions (music-automation-controls.md §3.1, D1) — the automation
/// counterpart to the chat-flavored <c>song_*</c> actions in this same folder. None post a chat reply;
/// the caller (a Stream Deck key, any other automation client, or a manually-built pipeline) reads
/// success/failure off the returned <see cref="ActionResult"/> only. <see cref="Category"/> is
/// "Music Control" across every action here so the pipeline builder groups them together.
/// </summary>
internal static class MusicControlResult
{
    public static ActionResult FromMusicResult(Result result, string successOutput) =>
        result.IsSuccess
            ? ActionResult.Success(successOutput)
            : ActionResult.Failure(
                result.ErrorCode ?? result.ErrorMessage ?? "music action failed"
            );

    /// <summary>Resolves the active provider key, or a typed failure when no provider is connected —
    /// every manage-API action needs this before it can call <see cref="IMusicProviderManageApi"/>.</summary>
    public static async Task<Result<string>> ResolveProviderAsync(
        IMusicService music,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string? provider = await music.GetActiveProviderKeyAsync(broadcasterId.ToString(), ct);
        return provider is null
            ? Result.Failure<string>("no active music provider", "CAPABILITY_UNSUPPORTED")
            : Result.Success(provider);
    }

    /// <summary>Resolves the current track's provider URI, or a typed failure when nothing is playing —
    /// every "act on the current track" action (save/playlist/follow) needs this.</summary>
    public static async Task<Result<NowPlaying>> ResolveNowPlayingAsync(
        IMusicService music,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        NowPlaying? nowPlaying = await music.GetNowPlayingAsync(broadcasterId.ToString(), ct);
        return nowPlaying is null
            ? Result.Failure<NowPlaying>("nothing is currently playing", "CAPABILITY_UNSUPPORTED")
            : Result.Success(nowPlaying);
    }
}

// ─── Transport ──────────────────────────────────────────────────────────────

public sealed class MusicPlayAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_play";
    public string Category => "Music Control";
    public string Description => "Resumes playback.";

    public MusicPlayAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result result = await _music.PlayAsync(ctx.BroadcasterId.ToString(), ctx.CancellationToken);
        return MusicControlResult.FromMusicResult(result, "playing");
    }
}

public sealed class MusicPauseAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_pause";
    public string Category => "Music Control";
    public string Description => "Pauses playback.";

    public MusicPauseAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result result = await _music.PauseAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "paused");
    }
}

public sealed class MusicPlayPauseAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_play_pause";
    public string Category => "Music Control";
    public string Description => "Toggles playback based on the current state.";

    public MusicPlayPauseAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        NowPlaying? nowPlaying = await _music.GetNowPlayingAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        Result result = nowPlaying is { IsPlaying: true }
            ? await _music.PauseAsync(ctx.BroadcasterId.ToString(), ctx.CancellationToken)
            : await _music.PlayAsync(ctx.BroadcasterId.ToString(), ctx.CancellationToken);
        return MusicControlResult.FromMusicResult(
            result,
            nowPlaying is { IsPlaying: true } ? "paused" : "playing"
        );
    }
}

public sealed class MusicNextAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_next";
    public string Category => "Music Control";
    public string Description => "Skips to the next track.";

    public MusicNextAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result result = await _music.SkipAsync(ctx.BroadcasterId.ToString(), ctx.CancellationToken);
        return MusicControlResult.FromMusicResult(result, "skipped");
    }
}

public sealed class MusicPreviousAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_previous";
    public string Category => "Music Control";
    public string Description => "Goes back to the previous track.";

    public MusicPreviousAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result result = await _music.PreviousAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "went to previous track");
    }
}

public sealed class MusicSetVolumeAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_set_volume";
    public string Category => "Music Control";
    public string Description => "Sets playback volume to a fixed level (0-100).";

    public MusicSetVolumeAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        int volume = ResolveIntParam(action, "volume", ctx.Variables);
        Result result = await _music.SetVolumeAsync(
            ctx.BroadcasterId.ToString(),
            volume,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"volume set to {volume}");
    }

    internal static int ResolveIntParam(
        ActionDefinition action,
        string key,
        Dictionary<string, string> vars
    )
    {
        string? raw = action.GetString(key);
        if (!string.IsNullOrEmpty(raw) && raw.StartsWith('{') && raw.EndsWith('}'))
            vars.TryGetValue(raw[1..^1], out raw);
        return int.TryParse(raw, out int value) ? value : action.GetInt(key);
    }
}

public sealed class MusicSeekAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_seek";
    public string Category => "Music Control";
    public string Description => "Jumps to a specific point in the current track (seconds).";

    public MusicSeekAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        int positionSeconds = MusicSetVolumeAction.ResolveIntParam(
            action,
            "positionSeconds",
            ctx.Variables
        );
        Result result = await _music.SeekAsync(
            ctx.BroadcasterId.ToString(),
            positionSeconds * 1000,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"seeked to {positionSeconds}s");
    }
}

// ─── Shuffle / repeat ───────────────────────────────────────────────────────

public sealed class MusicSetShuffleAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_set_shuffle";
    public string Category => "Music Control";
    public string Description => "Turns shuffle on or off.";

    public MusicSetShuffleAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        bool enabled = string.Equals(
            action.GetString("enabled"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        Result result = await _music.SetShuffleAsync(
            ctx.BroadcasterId.ToString(),
            enabled,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"shuffle {(enabled ? "on" : "off")}");
    }
}

public sealed class MusicToggleShuffleAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_toggle_shuffle";
    public string Category => "Music Control";
    public string Description => "Switches shuffle on/off based on the current state.";

    public MusicToggleShuffleAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<NowPlaying> nowPlaying = await MusicControlResult.ResolveNowPlayingAsync(
            _music,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (nowPlaying.IsFailure)
            return ActionResult.Failure(nowPlaying.ErrorCode ?? "CAPABILITY_UNSUPPORTED");

        bool newState = !nowPlaying.Value.ShuffleEnabled;
        Result result = await _music.SetShuffleAsync(
            ctx.BroadcasterId.ToString(),
            newState,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"shuffle {(newState ? "on" : "off")}");
    }
}

public sealed class MusicSetRepeatAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_set_repeat";
    public string Category => "Music Control";
    public string Description => "Sets repeat to Off, Track, or Playlist/Album.";

    public MusicSetRepeatAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string mode = action.GetString("mode") ?? "off";
        Result result = await _music.SetRepeatAsync(
            ctx.BroadcasterId.ToString(),
            mode,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"repeat mode set to {mode}");
    }
}

public sealed class MusicCycleRepeatAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_cycle_repeat";
    public string Category => "Music Control";
    public string Description =>
        "Cycles repeat Off -> Track -> Playlist/Album based on the current mode.";

    public MusicCycleRepeatAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<NowPlaying> nowPlaying = await MusicControlResult.ResolveNowPlayingAsync(
            _music,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (nowPlaying.IsFailure)
            return ActionResult.Failure(nowPlaying.ErrorCode ?? "CAPABILITY_UNSUPPORTED");

        MusicRepeatMode next = nowPlaying.Value.RepeatMode switch
        {
            MusicRepeatMode.Off => MusicRepeatMode.Track,
            MusicRepeatMode.Track => MusicRepeatMode.Context,
            _ => MusicRepeatMode.Off,
        };
        string mode = next.ToString().ToLowerInvariant();
        Result result = await _music.SetRepeatAsync(
            ctx.BroadcasterId.ToString(),
            mode,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"repeat mode set to {mode}");
    }
}

// ─── Device ─────────────────────────────────────────────────────────────────

public sealed class MusicTransferDeviceAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "music_transfer_device";
    public string Category => "Music Control";
    public string Description => "Moves playback to a chosen device.";

    public MusicTransferDeviceAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string deviceId = ResolveStringParam(action, "deviceId", ctx.Variables);
        if (string.IsNullOrWhiteSpace(deviceId))
            return ActionResult.Failure("music_transfer_device requires a non-empty 'deviceId'");

        Result result = await _music.TransferPlaybackAsync(
            ctx.BroadcasterId.ToString(),
            deviceId,
            true,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"transferred to device {deviceId}");
    }

    internal static string ResolveStringParam(
        ActionDefinition action,
        string key,
        Dictionary<string, string> vars
    )
    {
        string value = action.GetString(key) ?? string.Empty;
        if (value.StartsWith('{') && value.EndsWith('}'))
            vars.TryGetValue(value[1..^1], out value!);
        return value ?? string.Empty;
    }
}

// ─── Library / saved tracks ─────────────────────────────────────────────────

public sealed class MusicSaveTrackAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_save_track";
    public string Category => "Music Control";
    public string Description => "Adds the currently playing track to your Liked Songs.";

    public MusicSaveTrackAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        (Result<string> provider, Result<NowPlaying> nowPlaying) = await ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.SaveTracksAsync(
            ctx.BroadcasterId,
            provider.Value,
            [nowPlaying.Value.TrackUri],
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "track saved");
    }

    internal static async Task<(Result<string>, Result<NowPlaying>)> ResolveAsync(
        IMusicService music,
        PipelineExecutionContext ctx
    ) =>
        (
            await MusicControlResult.ResolveProviderAsync(
                music,
                ctx.BroadcasterId,
                ctx.CancellationToken
            ),
            await MusicControlResult.ResolveNowPlayingAsync(
                music,
                ctx.BroadcasterId,
                ctx.CancellationToken
            )
        );
}

public sealed class MusicUnsaveTrackAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_unsave_track";
    public string Category => "Music Control";
    public string Description => "Removes the currently playing track from your Liked Songs.";

    public MusicUnsaveTrackAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.RemoveSavedTracksAsync(
            ctx.BroadcasterId,
            provider.Value,
            [nowPlaying.Value.TrackUri],
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "track removed from saved");
    }
}

public sealed class MusicToggleSavedAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_toggle_saved";
    public string Category => "Music Control";
    public string Description =>
        "Adds/removes the current track from your Liked Songs based on whether it's already saved.";

    public MusicToggleSavedAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        string trackUri = nowPlaying.Value.TrackUri;
        Result<IReadOnlyList<bool>> savedCheck = await _manageApi.AreTracksSavedAsync(
            ctx.BroadcasterId,
            provider.Value,
            [trackUri],
            ctx.CancellationToken
        );
        if (savedCheck.IsFailure)
            return ActionResult.Failure(savedCheck.ErrorCode ?? "CAPABILITY_UNSUPPORTED");

        bool isSaved = savedCheck.Value.Count > 0 && savedCheck.Value[0];
        Result result = isSaved
            ? await _manageApi.RemoveSavedTracksAsync(
                ctx.BroadcasterId,
                provider.Value,
                [trackUri],
                ctx.CancellationToken
            )
            : await _manageApi.SaveTracksAsync(
                ctx.BroadcasterId,
                provider.Value,
                [trackUri],
                ctx.CancellationToken
            );
        return MusicControlResult.FromMusicResult(
            result,
            isSaved ? "track removed from saved" : "track saved"
        );
    }
}

// ─── Playlists ──────────────────────────────────────────────────────────────

public sealed class MusicAddToPlaylistAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_add_to_playlist";
    public string Category => "Music Control";
    public string Description => "Adds the currently playing track to a chosen playlist.";

    public MusicAddToPlaylistAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string playlistId = MusicTransferDeviceAction.ResolveStringParam(
            action,
            "playlistId",
            ctx.Variables
        );
        if (string.IsNullOrWhiteSpace(playlistId))
            return ActionResult.Failure("music_add_to_playlist requires a non-empty 'playlistId'");

        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.AddPlaylistTracksAsync(
            ctx.BroadcasterId,
            provider.Value,
            playlistId,
            [nowPlaying.Value.TrackUri],
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"added to playlist {playlistId}");
    }
}

public sealed class MusicRemoveFromPlaylistAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_remove_from_playlist";
    public string Category => "Music Control";
    public string Description => "Removes the currently playing track from a chosen playlist.";

    public MusicRemoveFromPlaylistAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string playlistId = MusicTransferDeviceAction.ResolveStringParam(
            action,
            "playlistId",
            ctx.Variables
        );
        if (string.IsNullOrWhiteSpace(playlistId))
            return ActionResult.Failure(
                "music_remove_from_playlist requires a non-empty 'playlistId'"
            );

        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.RemovePlaylistTracksAsync(
            ctx.BroadcasterId,
            provider.Value,
            playlistId,
            [nowPlaying.Value.TrackUri],
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, $"removed from playlist {playlistId}");
    }
}

// ─── Follow ─────────────────────────────────────────────────────────────────

public sealed class MusicFollowArtistAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_follow_artist";
    public string Category => "Music Control";
    public string Description => "Follows the current track's artist.";

    public MusicFollowArtistAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.ArtistId is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.FollowAsync(
            ctx.BroadcasterId,
            provider.Value,
            MusicFollowTarget.Artist,
            nowPlaying.Value.ArtistId,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "artist followed");
    }
}

public sealed class MusicUnfollowArtistAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manageApi;

    public string ActionType => "music_unfollow_artist";
    public string Category => "Music Control";
    public string Description => "Unfollows the current track's artist.";

    public MusicUnfollowArtistAction(IMusicService music, IMusicProviderManageApi manageApi)
    {
        _music = music;
        _manageApi = manageApi;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        (Result<string> provider, Result<NowPlaying> nowPlaying) =
            await MusicSaveTrackAction.ResolveAsync(_music, ctx);
        if (provider.IsFailure)
            return ActionResult.Failure(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");
        if (nowPlaying.IsFailure || nowPlaying.Value.ArtistId is null)
            return ActionResult.Failure("CAPABILITY_UNSUPPORTED");

        Result result = await _manageApi.UnfollowAsync(
            ctx.BroadcasterId,
            provider.Value,
            MusicFollowTarget.Artist,
            nowPlaying.Value.ArtistId,
            ctx.CancellationToken
        );
        return MusicControlResult.FromMusicResult(result, "artist unfollowed");
    }
}
