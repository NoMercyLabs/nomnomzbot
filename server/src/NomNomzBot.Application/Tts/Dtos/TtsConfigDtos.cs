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

/// <summary>TTS configuration for a channel.</summary>
public sealed record TtsConfigDto(
    bool IsEnabled,
    string DefaultVoiceId,
    int MaxLength,
    string MinPermission,
    bool SkipBotMessages,
    bool ReadUsernames
);

/// <summary>Request to update TTS configuration.</summary>
public sealed record UpdateTtsConfigDto
{
    public bool? IsEnabled { get; init; }

    [MaxLength(255)]
    public string? DefaultVoiceId { get; init; }

    [Range(1, 500)]
    public int? MaxLength { get; init; }

    [RegularExpression("^(everyone|subscribers|vip|moderators|broadcaster)$")]
    public string? MinPermission { get; init; }

    public bool? SkipBotMessages { get; init; }
    public bool? ReadUsernames { get; init; }
}
