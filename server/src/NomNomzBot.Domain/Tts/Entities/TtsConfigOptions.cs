// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Tts.Entities;

/// <summary>
/// The choices and limits of the TTS settings, in one place. The update DTO validates against these
/// and the options endpoint serves them, so a settings page lists exactly what the API accepts.
/// </summary>
public static class TtsConfigOptions
{
    public const int MaxCharactersLimit = 500;

    /// <summary>Seconds one viewer waits between spoken messages; 0 = no cooldown (default).</summary>
    public const int CooldownSecondsDefault = 0;

    /// <summary>Longest per-viewer cooldown a channel may set (one hour).</summary>
    public const int CooldownSecondsMax = 3600;

    public const string ProviderPattern = "^(edge|azure|elevenlabs)$";
    public const string ModePattern = "^(client_edge|byok|self_host)$";
    public const string MinPermissionPattern =
        "^(everyone|subscribers|vip|moderators|broadcaster)$";

    public static readonly IReadOnlyList<string> Providers = ["edge", "azure", "elevenlabs"];

    /// <summary>client_edge is retired (it reads as self_host), so it is accepted but never offered.</summary>
    public static readonly IReadOnlyList<string> Modes = ["self_host", "byok"];

    public static readonly IReadOnlyList<string> MinPermissions =
    [
        "everyone",
        "subscribers",
        "vip",
        "moderators",
        "broadcaster",
    ];
}
