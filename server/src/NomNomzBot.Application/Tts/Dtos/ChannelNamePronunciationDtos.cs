// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>A channel's own name and the way TTS says it, or null when TTS reads the name as written.</summary>
public sealed record ChannelNamePronunciationDto(string ChannelName, string? Pronunciation);

/// <summary>Request to set how TTS says the channel's name. Empty or blank clears it.</summary>
public sealed record SetChannelNamePronunciationDto
{
    [MaxLength(100)]
    public string? Pronunciation { get; init; }
}
