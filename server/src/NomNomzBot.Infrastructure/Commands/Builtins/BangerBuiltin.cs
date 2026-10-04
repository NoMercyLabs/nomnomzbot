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
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using ContractPlaylist = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!banger</c> — adds the playing track to the channel's banger playlist (its own choice, or one made on
/// first use when auto-create is on). A track already on the playlist is never added twice.
/// </summary>
public sealed class BangerBuiltin(
    IMusicService music,
    IMusicProviderManageApi playlists,
    IMusicConfigService config,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    private const string NoPlaylistCode = "NO_PLAYLIST";
    private const string DefaultPlaylistName = "Bangers";

    public string BuiltinKey => "banger";
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        NowPlaying? nowPlaying = await music.GetNowPlayingAsync(
            context.BroadcasterId.ToString(),
            ct
        );
        if (nowPlaying is null || string.IsNullOrWhiteSpace(nowPlaying.TrackUri))
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Banger.Nothing,
                "Nothing is playing right now.",
                [],
                ct
            );

        Dictionary<string, string> variables = new()
        {
            ["user"] = context.TriggeringUserDisplayName,
            ["track.name"] = nowPlaying.TrackName ?? nowPlaying.TrackUri,
        };

        Result<PlaylistTarget> target = await ResolveTargetAsync(context, nowPlaying, ct);
        if (target.IsFailure)
            return target.ErrorCode == NoPlaylistCode
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Banger.NoPlaylist,
                    "No banger playlist is chosen yet. Pick one in the music settings.",
                    [],
                    ct
                )
                : await FailedAsync(context, variables, ct);

        Result<bool> present = await playlists.IsTrackInPlaylistAsync(
            context.BroadcasterId,
            target.Value.Provider,
            target.Value.PlaylistId,
            nowPlaying.TrackUri,
            ct
        );
        if (present.IsFailure)
            return await FailedAsync(context, variables, ct);

        if (present.Value)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Banger.AlreadyThere,
                "@{user}, \"{track.name}\" is already in the bangers playlist.",
                variables,
                ct
            );

        Result added = await playlists.AddPlaylistTracksAsync(
            context.BroadcasterId,
            target.Value.Provider,
            target.Value.PlaylistId,
            [nowPlaying.TrackUri],
            ct
        );
        if (added.IsFailure)
            return await FailedAsync(context, variables, ct);

        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Banger.Added,
            "Added \"{track.name}\" to the bangers playlist!",
            variables,
            ct
        );
    }

    private async Task<Result<PlaylistTarget>> ResolveTargetAsync(
        BuiltinCommandContext context,
        NowPlaying nowPlaying,
        CancellationToken ct
    )
    {
        Result<MusicConfigDto> loaded = await config.GetConfigAsync(
            context.BroadcasterId.ToString(),
            ct
        );
        if (loaded.IsFailure)
            return Result.Failure<PlaylistTarget>(loaded.ErrorMessage ?? "Config unreadable.");

        MusicConfigDto settings = loaded.Value;
        string provider = settings.BangerPlaylistProvider ?? nowPlaying.Provider;

        if (!string.IsNullOrEmpty(settings.BangerPlaylistId))
            return Result.Success(new PlaylistTarget(provider, settings.BangerPlaylistId));

        if (!settings.BangerAutoCreate)
            return Result.Failure<PlaylistTarget>("No banger playlist is chosen.", NoPlaylistCode);

        Result<ContractPlaylist> created = await playlists.CreatePlaylistAsync(
            context.BroadcasterId,
            nowPlaying.Provider,
            new CreateMusicPlaylistDto { Name = DefaultPlaylistName },
            ct
        );
        if (created.IsFailure)
            return Result.Failure<PlaylistTarget>(created.ErrorMessage ?? "Create failed.");

        Result<MusicConfigDto> saved = await config.UpdateConfigAsync(
            context.BroadcasterId.ToString(),
            new UpdateMusicConfigDto
            {
                BangerPlaylistId = created.Value.Id,
                BangerPlaylistProvider = nowPlaying.Provider,
            },
            ct
        );
        if (saved.IsFailure)
            return Result.Failure<PlaylistTarget>(saved.ErrorMessage ?? "Save failed.");

        return Result.Success(new PlaylistTarget(nowPlaying.Provider, created.Value.Id));
    }

    private Task<Result<string>> FailedAsync(
        BuiltinCommandContext context,
        Dictionary<string, string> variables,
        CancellationToken ct
    ) =>
        ReplyAsync(
            context,
            BuiltinResponseSlots.Banger.Failed,
            "@{user}, the bangers playlist could not be updated. Try again in a moment.",
            variables,
            ct
        );

    private async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string fallback,
        Dictionary<string, string> variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Banger.Key,
                slot,
                fallback,
                variables,
                ct
            )
        );

    private sealed record PlaylistTarget(string Provider, string PlaylistId);
}
