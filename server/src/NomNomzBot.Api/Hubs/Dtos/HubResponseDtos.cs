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

/// <summary>Broadcast when the channel's title/category changes (<c>channel.update</c>) — keeps the stream-info card live.</summary>
public record StreamInfoChangedDto(
    string BroadcasterId,
    string BroadcasterDisplayName,
    string Title,
    string GameName
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

/// <summary>
/// <paramref name="TargetDisplayName"/>/<paramref name="TargetAvatarUrl"/>/<paramref name="TargetPronouns"/>/
/// <paramref name="TargetCommunityStanding"/> are additive hub-broadcast-layer enrichment
/// (<c>IHubUserEnricher</c>) for the moderated viewer — null when unavailable.
/// </summary>
public record ModActionDto(
    string Action,
    string ModeratorId,
    string TargetUserId,
    string? Reason,
    int? DurationSeconds,
    string? TargetDisplayName = null,
    string? TargetAvatarUrl = null,
    string? TargetPronouns = null,
    string? TargetCommunityStanding = null
);

public record CommandExecutedDto(
    string BroadcasterId,
    string CommandName,
    string TriggeredByUserId,
    bool Succeeded,
    string Timestamp
);

/// <summary>
/// <paramref name="AvatarUrl"/>/<paramref name="Pronouns"/>/<paramref name="CommunityStanding"/> are additive
/// hub-broadcast-layer enrichment (<c>IHubUserEnricher</c>) for the redeeming viewer — null when unavailable.
/// </summary>
public record RewardRedeemedDto(
    string BroadcasterId,
    string RewardId,
    string RewardTitle,
    string RedemptionId,
    string UserId,
    string UserDisplayName,
    int Cost,
    string? UserInput,
    string Timestamp,
    string? AvatarUrl = null,
    string? Pronouns = null,
    string? CommunityStanding = null
);

public record PermissionChangedDto(
    string SubjectType,
    string SubjectId,
    string ResourceType,
    string ResourceId,
    int Value
);

/// <summary>
/// Broadcast for reward CONFIG lifecycle (create/update/remove on Twitch) — distinct from
/// <see cref="RewardRedeemedDto"/>, which is a redemption. <paramref name="Action"/> is
/// <c>created</c> / <c>updated</c> / <c>removed</c>; <c>Cost</c>/<c>IsEnabled</c> are <c>null</c> when the
/// source event does not carry them (a removal only carries the reward's id/title).
/// </summary>
public record RewardChangedDto(
    string BroadcasterId,
    string Action,
    string RewardId,
    string Title,
    int? Cost,
    bool? IsEnabled,
    string Timestamp
);

/// <summary>
/// Generic dashboard-refresh signal (E5) for ANY config CRUD mutation (commands, timers, pipelines, TTS config,
/// webhooks, ...). <paramref name="Domain"/> matches the dashboard's config page/query key for that mutation; the
/// receiving client just refetches it. <paramref name="EntityId"/> is the affected row's id, or <c>null</c> for a
/// domain-wide change. <paramref name="Action"/> is <c>created</c> / <c>updated</c> / <c>deleted</c> / <c>toggled</c>.
/// </summary>
public record ConfigChangedDto(
    string BroadcasterId,
    string Domain,
    string? EntityId,
    string Action
);

// ─── Overlay / widget / OBS ──────────────────────────────────────────────────

public record WidgetEventDto(string WidgetId, string EventType, object? Data);

/// <summary>
/// One event on the generic overlay feed: the canonical <paramref name="Type"/> the overlay filters on, and the
/// event's data as a raw JSON string (<paramref name="Payload"/>) the overlay parses. Kept as a string to stay
/// serializer-agnostic on the wire — the client does a single <c>JSON.parse</c>.
/// </summary>
public record OverlayEventDto(string Type, string Payload);

public record WidgetSettingsDto(string WidgetId, object Settings);

/// <summary>Pushed to a widget's group when its compile-on-save build fails, so a live editor surfaces the error.</summary>
public record WidgetCompileFailedDto(string WidgetId, int VersionNumber, string BuildError);

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

// ─── TTS overlay (client_edge dispatch) ─────────────────────────────────────────

/// <summary>
/// Server-sent utterance the browser-source widget renders edge-side (tts.md §3.4 <c>client_edge</c> plane) — the
/// server never synthesizes or ships audio bytes; the overlay speaks the text with the resolved provider voice.
/// Owned here (the <c>IOverlayClient</c> contract lives in widgets-overlays); consumed by the TTS dispatcher.
/// </summary>
public record TtsSpeakPayload(
    Guid BroadcasterId,
    string Text,
    string VoiceId,
    string Provider,
    string? CueId,
    TtsSpeakOptions? Options
);

/// <summary>Optional prosody overrides for a client-edge utterance (all null = provider defaults).</summary>
public record TtsSpeakOptions(double? Rate, double? Pitch, double? Volume);
