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

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>One row of a bulk per-viewer voice-assignment import: the Twitch user id and the catalogue voice they get.</summary>
public sealed record TtsVoiceAssignmentRowDto
{
    [Required]
    [MaxLength(50)]
    public string TwitchUserId { get; init; } = null!;

    [Required]
    [MaxLength(255)]
    public string VoiceId { get; init; } = null!;

    /// <summary>
    /// Twitch login of the viewer, only consulted when the import runs with <c>createMissing</c>: an unknown
    /// Twitch user with a username is created as a bare viewer User (the chat-ingest get-or-create seam)
    /// instead of being skipped. Optional — omitting it keeps the row skip-on-unknown.
    /// </summary>
    [MaxLength(100)]
    public string? Username { get; init; }
}

/// <summary>Why one import row was skipped instead of applied (<c>unknown_user</c> | <c>unknown_voice</c>).</summary>
public sealed record TtsVoiceImportSkipDto(string TwitchUserId, string Reason);

/// <summary>The bulk-import outcome: how many assignments were upserted and every row that was skipped, with its reason.</summary>
public sealed record TtsVoiceImportResultDto(
    int Imported,
    IReadOnlyList<TtsVoiceImportSkipDto> Skipped
);
