// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// Mainstream platforms a real viewer links in ordinary chat ("go follow the raid target at
/// twitch.tv/x", a gist of code). A storefront is never one of these, so they never carry the
/// selling-audience signal, and a link to one needs no link capability.
/// </summary>
public static class MainstreamHosts
{
    private static readonly string[] Domains =
    [
        "twitch.tv",
        "youtube.com",
        "youtu.be",
        "twitter.com",
        "x.com",
        "instagram.com",
        "tiktok.com",
        "reddit.com",
        "discord.gg",
        "discord.com",
        "kick.com",
        "streamelements.com",
        "streamlabs.com",
        "throne.com",
        "ko-fi.com",
        "patreon.com",
        "spotify.com",
        "github.com",
    ];

    /// <summary>True when <paramref name="host"/> is, or is a subdomain of, a mainstream domain or one of <paramref name="channelAllowed"/>.</summary>
    public static bool IsMainstreamOrAllowed(string host, IEnumerable<string> channelAllowed) =>
        Domains
            .Concat(channelAllowed)
            .Any(allowed =>
                host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase)
            );
}
