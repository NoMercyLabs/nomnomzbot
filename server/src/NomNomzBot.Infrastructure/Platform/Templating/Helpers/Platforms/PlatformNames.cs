// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>The fixed facts about each streaming platform: display name, icon and the address of a channel.</summary>
internal static class PlatformNames
{
    internal sealed record Info(string Key, string Name, string Icon);

    private static readonly Info[] Known =
    [
        new(AuthEnums.Platform.Twitch, "Twitch", "🟣"),
        new(AuthEnums.Platform.Kick, "Kick", "🟢"),
        new(AuthEnums.Platform.YouTube, "YouTube", "🔴"),
    ];

    /// <summary>The platform behind a provider key or a display name, or null when it is not a known platform.</summary>
    public static Info? Find(string? text) =>
        Known.FirstOrDefault(i =>
            string.Equals(i.Key, text?.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Name, text?.Trim(), StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>The place of the platform in every list (Twitch, Kick, YouTube).</summary>
    public static int OrderOf(string provider) =>
        Array.FindIndex(
            Known,
            i => string.Equals(i.Key, provider, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>The address of the channel without a scheme, for example <c>kick.com/samplechannel</c>.</summary>
    public static string? Address(PlatformLink link) =>
        Find(link.Provider)?.Key switch
        {
            AuthEnums.Platform.Twitch => "twitch.tv/" + link.Slug,
            AuthEnums.Platform.Kick => "kick.com/" + link.Slug,
            AuthEnums.Platform.YouTube when link.ExternalChannelId.Length > 0 =>
                "youtube.com/channel/" + link.ExternalChannelId,
            _ => null,
        };

    public static string? Url(PlatformLink link) =>
        Address(link) is { } address ? "https://" + address : null;
}
