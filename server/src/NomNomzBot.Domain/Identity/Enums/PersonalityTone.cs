// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Enums;

/// <summary>
/// Canonical string constants for a channel's <c>Personality</c> tone — the voice its built-in commands
/// answer in. Stored as the schema's readable <c>string</c> token (the [VC:enum] convention shared with
/// <see cref="AuthEnums"/>), not a CLR enum, so the DB carries the token and this set is the single source
/// of truth for every producer/consumer.
///
/// <para>
/// <see cref="Informative"/> is the default — clear and polite, NOT sassy. Sassy is one opt-in tone among
/// five. Each tone maps to a variation-set of built-in response templates in the tone catalog.
/// </para>
/// </summary>
public static class PersonalityTone
{
    /// <summary>Clear, helpful, polite. The default voice for every new and existing channel.</summary>
    public const string Informative = "informative";

    /// <summary>Warm, wholesome, encouraging.</summary>
    public const string Friendly = "friendly";

    /// <summary>Witty, teasing, playful — never mean.</summary>
    public const string Sassy = "sassy";

    /// <summary>High-energy, caps, emotes.</summary>
    public const string Hype = "hype";

    /// <summary>Laid-back, minimal.</summary>
    public const string Chill = "chill";

    /// <summary>The tone a channel falls back to whenever none is set — always <see cref="Informative"/>.</summary>
    public const string Default = Informative;

    /// <summary>Every valid tone token, in catalogue order (Informative first — it is the default).</summary>
    public static readonly IReadOnlyList<string> All = [Informative, Friendly, Sassy, Hype, Chill];

    /// <summary>True when <paramref name="tone"/> is one of the five defined tones (case-insensitive).</summary>
    public static bool IsValid(string? tone) =>
        tone is not null && All.Contains(tone, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes an incoming tone token to its canonical lowercase form, or <see cref="Default"/> when the
    /// value is null/blank/unrecognized — so a resolved tone is always a valid catalog key.
    /// </summary>
    public static string Normalize(string? tone)
    {
        if (string.IsNullOrWhiteSpace(tone))
            return Default;

        foreach (string candidate in All)
        {
            if (candidate.Equals(tone, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return Default;
    }
}
