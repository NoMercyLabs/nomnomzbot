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
