// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Dtos;

// ─── Stream / music state ─────────────────────────────────────────────────────

public record StreamStatusDto(
    bool IsLive,
    string? StreamId,
    string? Title,
    string? GameName,
    string? StartedAt
);

public record MusicStateDto(bool IsPlaying, MusicTrackDto? CurrentTrack);

public record MusicTrackDto(
    string TrackName,
    string Artist,
    string Album,
    string? AlbumArtUrl,
    int DurationMs,
    string Provider
);

// ─── Action DTOs ─────────────────────────────────────────────────────────────

public record ModActionDto(
    string Action,
    string ModeratorId,
    string TargetUserId,
    string? Reason,
    int? DurationSeconds
);

public record CommandExecutedDto(
    string BroadcasterId,
    string CommandName,
    string TriggeredByUserId,
    bool Succeeded,
    string Timestamp
);

public record RewardRedeemedDto(
    string BroadcasterId,
    string RewardId,
    string RewardTitle,
    string RedemptionId,
    string UserId,
    string UserDisplayName,
    int Cost,
    string? UserInput,
    string Timestamp
);

public record PermissionChangedDto(
    string SubjectType,
    string SubjectId,
    string ResourceType,
    string ResourceId,
    int Value
);

// ─── Overlay / widget / OBS ──────────────────────────────────────────────────

public record WidgetEventDto(string WidgetId, string EventType, object? Data);

public record WidgetSettingsDto(string WidgetId, object Settings);

public record OBSCommandDto(string RequestId, string Command, object? Params);

public record OBSResponseDto(string RequestId, bool Success, object? Data, string? Error);

public record OBSStateUpdateDto(string State, object? Data);

public record OBSConnectedDto(string BroadcasterId, string Version);

// ─── Hub response DTOs ────────────────────────────────────────────────────────

public record JoinChannelResponse(bool Success, string? Error, StreamStatusDto? StreamStatus);

public record SendMessageResponse(bool Success, string? Error, string? MessageId);

public record ActionResponse(bool Success, string? Error);

public record JoinWidgetResponse(bool Success, string? Error, object? InitialState);

// ─── Sound overlay ────────────────────────────────────────────────────────────

/// <summary>Payload the overlay receives to start playback of a clip on its audio bus.</summary>
public record PlaySoundPayload(string PlaybackUrl, int Volume, string? Handle);

/// <summary>Payload the overlay receives to stop in-progress clip playback.</summary>
public record StopSoundPayload(string? Handle, bool All);
