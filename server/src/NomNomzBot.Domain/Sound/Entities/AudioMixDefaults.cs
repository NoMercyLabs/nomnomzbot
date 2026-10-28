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

namespace NomNomzBot.Domain.Sound.Entities;

/// <summary>
/// The one place each audio-mix default and limit is written. A channel with no saved row resolves to these
/// values; the entity, the service, the validation and the API response all read them, so the dashboard
/// never carries a second copy.
/// </summary>
public static class AudioMixDefaults
{
    /// <summary>Master volume of a channel with no saved mix.</summary>
    public const int MasterVolume = 100;

    /// <summary>Text-to-speech volume of a channel with no saved mix.</summary>
    public const int TtsVolume = 100;

    /// <summary>
    /// Share of the music level that stays while bot audio speaks, 0..100. 100 means the music is not ducked.
    /// </summary>
    public const int MusicDuckPercent = 100;

    /// <summary>Lowest accepted volume (silent).</summary>
    public const int MinVolume = 0;

    /// <summary>Highest accepted volume.</summary>
    public const int MaxVolume = 100;
}
