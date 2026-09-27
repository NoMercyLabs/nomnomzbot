// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
