// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>The shared reads and prints of the platform rows.</summary>
internal static class PlatformText
{
    /// <summary>What a platform that is not connected, or not live, prints: nothing.</summary>
    public const string Nothing = "";

    public const string ListSeparator = ", ";

    public const string PartSeparator = " · ";

    /// <summary>The connections that are live now, in platform order.</summary>
    public static async Task<List<(PlatformLink Link, PlatformStatus Status)>> LiveAsync(
        PlatformData data
    ) => [.. (await data.StatusesAsync()).Where(c => c.Status.IsLive)];

    /// <summary>The live connections that report a viewer count, with that count.</summary>
    public static async Task<List<(PlatformLink Link, int Viewers)>> WithViewersAsync(
        PlatformData data
    ) =>
        [
            .. (await LiveAsync(data))
                .Where(c => c.Status.Viewers.HasValue)
                .Select(c => (c.Link, c.Status.Viewers!.Value)),
        ];

    public static string NameOf(PlatformLink link) =>
        PlatformNames.Find(link.Provider)?.Name ?? link.Provider;

    public static string Count(PlatformCall call, long value) =>
        PeopleText.Count(value, call.Culture);

    public static string? NothingIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Nothing : text;
}
