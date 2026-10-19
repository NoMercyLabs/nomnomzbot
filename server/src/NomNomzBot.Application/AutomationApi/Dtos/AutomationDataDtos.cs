// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>
/// The authenticated identity behind a data-plane call — resolved from a presented token secret.
/// Everything downstream trusts these fields, never the raw secret.
/// </summary>
public sealed record AutomationPrincipal(
    Guid BroadcasterId,
    Guid TokenId,
    string TokenName,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<BindingTarget>? AllowedTargets
)
{
    /// <summary>
    /// Whether this token may invoke <paramref name="target"/>. No pinned list (null or empty) allows every target;
    /// otherwise the kind must match and so must the id (an action: its action type), ignoring case.
    /// </summary>
    public bool Allows(BindingTarget target) =>
        AllowedTargets is not { Count: > 0 } allowed
        || allowed.Any(a =>
            a.Kind == target.Kind
            && string.Equals(
                a.Kind == BindingTargetKind.Action ? a.ActionType : a.Id,
                target.Kind == BindingTargetKind.Action ? target.ActionType : target.Id,
                StringComparison.OrdinalIgnoreCase
            )
        );
}

/// <summary>
/// Data-plane invocation. Name the target with <see cref="Kind"/> + <see cref="Id"/> (+ <see cref="ActionType"/> and
/// <see cref="Params"/> for an action), or the older way: a pipeline by id or by (unique) name.
/// </summary>
public sealed record AutomationInvokeRequest
{
    /// <summary><c>command | pipeline | action</c>; when set it wins over <see cref="PipelineId"/>/<see cref="PipelineName"/>.</summary>
    [MaxLength(20)]
    public string? Kind { get; init; }

    /// <summary>The command id (or built-in key) or the pipeline id (a guid).</summary>
    [MaxLength(200)]
    public string? Id { get; init; }

    [MaxLength(200)]
    public string? ActionType { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Params { get; init; }

    public Guid? PipelineId { get; init; }

    [MaxLength(200)]
    public string? PipelineName { get; init; }

    public IReadOnlyList<string>? Args { get; init; }

    public IDictionary<string, string>? Variables { get; init; }
}

/// <summary>
/// The invoke outcome. Execution is fire-and-forget (automation-api.md D5): <see cref="Accepted"/>
/// means enqueued, and <see cref="ExecutionId"/> is the correlation id minted at enqueue time (also
/// visible to the pipeline as <c>{{automation.correlation_id}}</c>) — not a promise of completion.
/// </summary>
/// <param name="PipelineId">The pipeline that was started; null when the target was not a pipeline.</param>
/// <param name="ExecutionId">The correlation id minted at enqueue time.</param>
/// <param name="Accepted">True when the run was enqueued.</param>
/// <param name="Kind">The target kind that was run: <c>command | pipeline | action</c>.</param>
/// <param name="TargetId">The command or pipeline id, or the action type.</param>
public sealed record AutomationInvokeResult(
    Guid? PipelineId,
    Guid ExecutionId,
    bool Accepted,
    string? Kind = null,
    string? TargetId = null
);

/// <summary>Chat send: plain message, reply (with <see cref="ReplyToMessageId"/>), or whisper.</summary>
public sealed record AutomationChatRequest
{
    [Required]
    [MaxLength(500)]
    public string Text { get; init; } = null!;

    [MaxLength(255)]
    public string? ReplyToMessageId { get; init; }

    [MaxLength(50)]
    public string? WhisperToTwitchUserId { get; init; }
}

/// <summary>An invocable pipeline as the data plane lists it.</summary>
public sealed record AutomationPipelineRef(Guid Id, string Name);

/// <summary>A chat command as the data plane lists it.</summary>
public sealed record AutomationCommandRef(string Name, IReadOnlyList<string> Aliases);

/// <summary>Broadcaster + instance summary for <c>GET /automation/v1/info</c>.</summary>
public sealed record AutomationInfo(
    Guid ChannelId,
    string ChannelName,
    string Provider,
    string ApiVersion
);

/// <summary>One subscribable public event type (automation-api.md §3, for the events catalog).</summary>
public sealed record AutomationEventCatalogItem(string PublicName, string Description);

/// <summary>
/// Current playback state (music-automation-controls.md §3.2/§3.3) — the position-anchor shape a
/// client extrapolates locally (widget-sdk.md §9), shared verbatim between the <c>GET
/// /automation/v1/music/now-playing</c> read and the <c>song.changed</c> event payload so they can
/// never drift (produced by the single Infrastructure-layer MusicAutomationProjection helper).
/// </summary>
public sealed record AutomationNowPlayingDto(
    string? Title,
    string? Artist,
    int DurationMs,
    int PositionMs,
    bool IsPlaying,
    bool ShuffleEnabled,
    string RepeatMode,
    bool? IsSaved,
    DateTimeOffset ServerTime,
    int VolumePercent,
    string? AlbumArtUrl,
    bool CanSetShuffle = true,
    bool CanSetRepeat = true,
    bool CanSkipNext = true,
    bool CanSkipPrevious = true,
    bool CanSeek = true,
    bool CanPause = true,
    bool CanResume = true
);

/// <summary>One of the broadcaster's playback devices on the active music provider.</summary>
public sealed record AutomationDeviceDto(
    string Id,
    string Name,
    string Type,
    bool IsActive,
    int? VolumePercent
);

/// <summary>One of the broadcaster's playlists on the active music provider.</summary>
public sealed record AutomationPlaylistDto(
    string Id,
    string Name,
    string Uri,
    int TrackCount,
    string? ImageUrl
);

/// <summary>
/// One OBS scene, as the data plane lists it for a property-inspector picker (S-STREAMDECK-OBS-REMAINDER)
/// — mirrors <see cref="NomNomzBot.Application.Obs.Dtos.ObsSceneDto"/>, kept as its own type so the
/// automation-plane contract can evolve independently of the dashboard-facing OBS DTOs.
/// </summary>
public sealed record AutomationObsSceneDto(string Name, bool IsCurrent);

/// <summary>One OBS input (audio/video source), as the data plane lists it for a property-inspector
/// picker — mirrors <see cref="NomNomzBot.Application.Obs.Dtos.ObsInputDto"/>.</summary>
public sealed record AutomationObsInputDto(string Name, string Kind, bool? Muted, double? VolumeDb);

/// <summary>One item placed in an OBS scene, as the data plane lists it for a property-inspector
/// picker (S-STREAMDECK-OBS-REMAINDER) — mirrors
/// <see cref="NomNomzBot.Application.Obs.Dtos.ObsSceneItemDto"/>.</summary>
public sealed record AutomationObsSceneItemDto(int SceneItemId, string SourceName, bool Enabled);

/// <summary>One OBS scene transition, as the data plane lists it for a property-inspector picker —
/// mirrors <see cref="NomNomzBot.Application.Obs.Dtos.ObsTransitionDto"/>.</summary>
public sealed record AutomationObsTransitionDto(string Name, bool IsCurrent);

/// <summary>One filter attached to an OBS source, as the data plane lists it for a property-inspector
/// picker — mirrors <see cref="NomNomzBot.Application.Obs.Dtos.ObsFilterDto"/>.</summary>
public sealed record AutomationObsFilterDto(string Name, string Kind, bool Enabled, int Index);

/// <summary>The <c>obs.streaming.changed</c> event payload (S-STREAMDECK-OBS-REMAINDER) — mirrors
/// <see cref="NomNomzBot.Domain.Obs.Events.ObsStreamingStateChangedEvent"/> field-for-field.</summary>
public sealed record AutomationObsStreamingStateDto(bool Active);

/// <summary>The <c>obs.recording.changed</c> event payload — mirrors
/// <see cref="NomNomzBot.Domain.Obs.Events.ObsRecordingStateChangedEvent"/> field-for-field.</summary>
public sealed record AutomationObsRecordingStateDto(bool Active, bool Paused);

/// <summary>The <c>obs.mute.changed</c> event payload — mirrors
/// <see cref="NomNomzBot.Domain.Obs.Events.ObsInputMuteStateChangedEvent"/> field-for-field.</summary>
public sealed record AutomationObsMuteStateDto(string InputName, bool Muted);

/// <summary>
/// Live streaming/recording state (S-STREAMDECK-OBS-REMAINDER) — the one-shot seed a Stream Deck key
/// fetches right after connecting/reconnecting, before the first <c>obs.streaming.changed</c>/
/// <c>obs.recording.changed</c> push arrives, mirroring how <see cref="AutomationNowPlayingDto"/> seeds
/// the music tiles. Mirrors <see cref="NomNomzBot.Application.Obs.Dtos.ObsStateDto"/>'s output fields
/// only — <c>CurrentScene</c>/<c>ReplayBufferActive</c>/<c>RecordTimecode</c> are out of this slice's
/// scope (no Stream Deck tile renders them live yet).
/// </summary>
public sealed record AutomationObsStateDto(bool Streaming, bool Recording, bool RecordPaused);
