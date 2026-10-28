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

namespace NomNomzBot.Application.PlatformDefaults.Dtos;

/// <summary>
/// The platform default TTS voice as the admin editor shows it: the catalogue voice every channel without its
/// own pick speaks with, how many channels follow it and how many picked their own.
/// </summary>
public sealed record TtsVoiceDefaultDto(
    string VoiceId,
    string DisplayName,
    string Locale,
    string Provider,
    int ChannelsFollowing,
    int ChannelsWithOwnVoice
);

/// <summary>
/// One voice the platform default may be set to: a catalogue voice every channel can speak without a key of
/// its own. <paramref name="IsDefault"/> marks the current default.
/// </summary>
public sealed record TtsVoiceCandidateDto(
    string VoiceId,
    string DisplayName,
    string Locale,
    string Gender,
    bool IsDefault
);

/// <summary>A proposed platform default voice — the preview body.</summary>
public sealed record TtsVoiceDefaultChange(string VoiceId);

/// <summary>Saves the platform default voice. <paramref name="ConfirmedChannelsAffected"/> must equal the preview's live count.</summary>
public sealed record SetTtsVoiceDefaultRequest(string VoiceId, int ConfirmedChannelsAffected);
