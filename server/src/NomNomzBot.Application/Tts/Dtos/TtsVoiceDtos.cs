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

/// <summary>An available TTS voice, with the catalogue metadata that powers search/filter and preview-before-pick.</summary>
public sealed record TtsVoiceDto(
    string Id,
    string Name,
    string DisplayName,
    string Locale,
    string Gender,
    string Provider,
    bool IsDefault,
    string? Accent,
    string? Age,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> Tags,
    string? Description,
    string? PreviewUrl
);

/// <summary>
/// A catalogue search: free-text <see cref="Q"/> matches name/display-name/description/tags; the rest are
/// equality filters. Ordered provider→locale→name, paged. An all-null query returns the first page whole.
/// </summary>
public sealed record TtsVoiceQuery(
    string? Q = null,
    string? Locale = null,
    string? Gender = null,
    string? Provider = null,
    string? Accent = null,
    int Page = 1,
    int PageSize = 50
);

/// <summary>Request to test a TTS voice.</summary>
public sealed record TtsTestRequestDto
{
    [Required, MaxLength(500)]
    public required string Text { get; init; }

    [Required, MaxLength(255)]
    public required string VoiceId { get; init; }
}

/// <summary>Result of a TTS test synthesis.</summary>
public sealed record TtsTestResultDto(
    string VoiceId,
    string Provider,
    int DurationMs,
    string AudioBase64
);
