// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    /// <summary>Lowest accepted volume (silent).</summary>
    public const int MinVolume = 0;

    /// <summary>Highest accepted volume.</summary>
    public const int MaxVolume = 100;
}
