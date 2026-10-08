// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The one place a renamed first-party widget key keeps resolving. A retired key maps to its canonical key, so a
/// channel that still holds the old gallery row, or an old OBS page, keeps working until the seeder has renamed it.
/// </summary>
public static class WidgetKeyAliases
{
    /// <summary>The canonical key of the Audio widget — the one page every sound and TTS line plays on.</summary>
    public const string Audio = "audio";

    /// <summary>Retired key to canonical key. The Audio widget was first shipped as <c>tts_audio</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> RetiredToCanonical = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["tts_audio"] = Audio,
    };

    /// <summary>The canonical key for a key; an unknown or null key comes back unchanged.</summary>
    public static string? Canonical(string? key) =>
        key is not null && RetiredToCanonical.TryGetValue(key, out string? canonical)
            ? canonical
            : key;

    /// <summary>The canonical key first, then every retired key that maps to it. Use for database lookups.</summary>
    public static IReadOnlyList<string> KeysFor(string key)
    {
        string canonical = Canonical(key)!;
        List<string> keys = [canonical];
        keys.AddRange(
            RetiredToCanonical
                .Where(pair => string.Equals(pair.Value, canonical, StringComparison.Ordinal))
                .Select(pair => pair.Key)
        );
        return keys;
    }
}
