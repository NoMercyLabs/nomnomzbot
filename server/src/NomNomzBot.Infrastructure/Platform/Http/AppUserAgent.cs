// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Infrastructure.Platform.Http;

/// <summary>
/// The product User-Agent every outbound <see cref="System.Net.Http.HttpClient"/> sends by default — wired
/// once via <c>ConfigureHttpClientDefaults</c> so provider fetches, OAuth, Twitch, TTS and webhook calls all
/// identify themselves uniformly. Stamped with the real build version so a provider (or our own logs) can see
/// exactly which NomNomzBot release made the request. A client may still override its own User-Agent.
/// </summary>
internal static class AppUserAgent
{
    /// <summary>The product token, e.g. <c>NomNomzBot/1.4.0</c> (build metadata such as <c>+&lt;gitsha&gt;</c> trimmed off).</summary>
    public static readonly string Value = Build();

    private static string Build()
    {
        Assembly assembly = typeof(AppUserAgent).Assembly;
        string version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Drop build metadata (e.g. "+<gitsha>") so the product token stays clean.
        int plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus];

        return $"NomNomzBot/{version}";
    }
}
