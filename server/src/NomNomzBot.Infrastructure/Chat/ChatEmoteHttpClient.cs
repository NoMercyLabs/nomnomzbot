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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The named <see cref="System.Net.Http.HttpClient"/> the third-party emote/badge adapters fetch through
/// (chat-decoration spec §7). The targets are fixed public provider APIs (BTTV/FFZ/7TV), not user-supplied URLs,
/// so this is a plain resilient client — distinct from the SSRF-hardened egress client used for link previews.
/// </summary>
internal static class ChatEmoteHttpClient
{
    public const string Name = "chat-emote-providers";

    /// <summary>A static User-Agent for the outbound provider calls, stamped with the real build version.</summary>
    public static readonly string UserAgent = BuildUserAgent();

    private static string BuildUserAgent()
    {
        Assembly assembly = typeof(ChatEmoteHttpClient).Assembly;
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
