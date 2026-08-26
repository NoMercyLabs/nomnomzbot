// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Services;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// Counts what stops working when a provider is disconnected. The dependency that matters is NOT a deleted
/// row — a disconnect deletes one connection and leaves everything else in place, silently dead. So this
/// counts the automation that loses its provider: every pipeline step whose ACTION belongs to that provider,
/// plus the supporter feeds that ingest through its connection.
/// </summary>
public sealed class IntegrationBlastRadiusService : IIntegrationBlastRadiusService
{
    /// <summary>Playback and song-request actions — both music providers drive the same action set.</summary>
    private static readonly string[] MusicActionTypes =
    [
        "music_add_to_playlist",
        "music_cycle_repeat",
        "music_follow_artist",
        "music_next",
        "music_pause",
        "music_play",
        "music_play_pause",
        "music_previous",
        "music_remove_from_playlist",
        "music_save_track",
        "music_seek",
        "music_set_repeat",
        "music_set_shuffle",
        "music_set_volume",
        "music_toggle_saved",
        "music_toggle_shuffle",
        "music_transfer_device",
        "music_unfollow_artist",
        "music_unsave_track",
        "music_volume_down",
        "music_volume_mute",
        "music_volume_up",
        "playlist_add",
        "song_ban",
        "song_current",
        "song_pause",
        "song_previous",
        "song_queue",
        "song_request",
        "song_resume",
        "song_skip",
        "song_volume",
        "song_wrong",
    ];

    /// <summary>
    /// The pipeline actions each provider owns, taken from the registered <c>ICommandAction.ActionType</c>
    /// values. A provider absent from this map owns no pipeline action; its blast radius is then whatever
    /// supporter ingest it carries, and a zero there is a real zero.
    /// </summary>
    private static readonly Dictionary<string, string[]> ProviderActionTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["spotify"] = MusicActionTypes,
        ["youtube"] = MusicActionTypes,
        ["discord"] = ["send_discord_notification"],
        ["obs"] =
        [
            "obs_call_vendor",
            "obs_filter",
            "obs_hotkey",
            "obs_input_mute",
            "obs_input_volume",
            "obs_media",
            "obs_recording",
            "obs_refresh_browser",
            "obs_replay_buffer",
            "obs_request",
            "obs_request_batch",
            "obs_save_replay",
            "obs_screenshot",
            "obs_set_preview_scene",
            "obs_set_source",
            "obs_streaming",
            "obs_switch_scene",
            "obs_transition",
            "obs_virtual_cam",
        ],
        ["vtube_studio"] =
        [
            "vts_color_tint",
            "vts_load_model",
            "vts_move_model",
            "vts_request",
            "vts_set_expression",
            "vts_trigger_hotkey",
        ],
    };

    private readonly IApplicationDbContext _db;
    private readonly IPipelineStepReferenceScanner _stepReferences;

    public IntegrationBlastRadiusService(
        IApplicationDbContext db,
        IPipelineStepReferenceScanner stepReferences
    )
    {
        _db = db;
        _stepReferences = stepReferences;
    }

    public async Task<Result<BlastRadiusDto>> GetDisconnectBlastRadiusAsync(
        Guid broadcasterId,
        string integrationId,
        CancellationToken ct = default
    )
    {
        string provider = integrationId.Trim().ToLowerInvariant();
        if (provider.Length == 0)
            return Result<BlastRadiusDto>.Failure(
                "An integration id is required.",
                "VALIDATION_FAILED"
            );

        if (!await IsConnectedAsync(broadcasterId, provider, ct))
            return Result<BlastRadiusDto>.Failure(
                $"Integration '{integrationId}' is not connected.",
                "NOT_FOUND"
            );

        List<BlastRadiusCategoryDto> categories = [];
        bool isMinimum = false;

        if (ProviderActionTypes.TryGetValue(provider, out string[]? actionTypes))
        {
            Result<PipelineStepReferenceScan> scan = await _stepReferences.CountByActionTypesAsync(
                broadcasterId,
                actionTypes,
                ct
            );
            if (scan.IsFailure)
                return Result<BlastRadiusDto>.Failure(
                    scan.ErrorMessage ?? "The reference scan failed.",
                    scan.ErrorCode ?? "SCAN_FAILED"
                );

            isMinimum = scan.Value.IsMinimum;
            if (scan.Value.MatchCount > 0)
                categories.Add(
                    new BlastRadiusCategoryDto(
                        BlastRadiusCategoryKeys.PipelineSteps,
                        scan.Value.MatchCount,
                        scan.Value.PipelineNames
                    )
                );
        }

        int supporterFeeds = await _db.SupporterConnections.CountAsync(
            connection =>
                connection.BroadcasterId == broadcasterId && connection.SourceKey == provider,
            ct
        );

        if (supporterFeeds > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.SupporterConnections,
                    supporterFeeds,
                    []
                )
            );

        return Result<BlastRadiusDto>.Success(new BlastRadiusDto(categories, isMinimum));
    }

    /// <summary>
    /// A provider is connected when it holds a live vault connection OR a legacy <c>Service</c> row — the two
    /// custody shapes the disconnect endpoint itself accepts. Discord is connected per guild instead.
    /// </summary>
    private async Task<bool> IsConnectedAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken ct
    )
    {
        if (provider == "discord")
            return await _db.DiscordGuildConnections.AnyAsync(
                connection => connection.BroadcasterId == broadcasterId,
                ct
            );

        bool vaulted = await _db.IntegrationConnections.AnyAsync(
            connection =>
                connection.BroadcasterId == broadcasterId
                && connection.Provider.ToLower() == provider
                && connection.Status != "revoked",
            ct
        );
        if (vaulted)
            return true;

        return await _db.Services.AnyAsync(
            service => service.BroadcasterId == broadcasterId && service.Name.ToLower() == provider,
            ct
        );
    }
}
