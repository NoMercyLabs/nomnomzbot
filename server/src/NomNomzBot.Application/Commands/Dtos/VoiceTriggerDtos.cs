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

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>One voice-triggered word counter + sticker.</summary>
public sealed record VoiceTriggerDto(
    Guid Id,
    string Word,
    bool IsEnabled,
    int StartingCount,
    int CurrentCount,
    int CooldownSeconds,
    Guid? StickerAssetId,
    string? StickerImageUrl,
    DateTime? LastFiredAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateVoiceTriggerRequest
{
    [Required]
    [MaxLength(200)]
    public required string Word { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Seeds the live count — e.g. the number already tracked by a bot being migrated away from.</summary>
    [Range(0, int.MaxValue)]
    public int StartingCount { get; init; }

    /// <summary>Cooldown between fires (spam/stutter guard, default 5s, capped 1h).</summary>
    [Range(0, 3600)]
    public int CooldownSeconds { get; init; } = 5;

    public Guid? StickerAssetId { get; init; }
}

public sealed record UpdateVoiceTriggerRequest
{
    [MaxLength(200)]
    public string? Word { get; init; }

    public bool? IsEnabled { get; init; }

    [Range(0, 3600)]
    public int? CooldownSeconds { get; init; }

    /// <summary>Absent leaves the sticker unchanged; <see cref="Guid.Empty"/> clears it; any other id binds it.</summary>
    public Guid? StickerAssetId { get; init; }
}

/// <summary>What the voice-listener page POSTs when it hears a defined word.</summary>
public sealed record ReportVoiceTriggerRequest
{
    [Required]
    [MaxLength(200)]
    public required string Transcript { get; init; }
}

public sealed record VoiceTriggerReportResultDto(bool Fired, string? Word, int? NewCount);

/// <summary>One finalized chunk of a stream's spoken-word transcript (backend `VoiceTranscriptSegment`).</summary>
public sealed record VoiceTranscriptSegmentDto(string Text, DateTimeOffset SpokenAt);
