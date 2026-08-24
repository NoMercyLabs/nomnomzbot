// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Resolves the first-party widget `.vue` source directory from the test binary's output folder — walks up from
/// <see cref="AppContext.BaseDirectory"/> until the <c>src/NomNomzBot.Infrastructure/Content/Widgets/Assets</c>
/// tree is found, so the contract test reads the SAME source the build embeds, not a copy.
/// </summary>
internal static class WidgetAssetPaths
{
    private const string RelativeAssetsPath =
        "src/NomNomzBot.Infrastructure/Content/Widgets/Assets";

    public static string AssetsDirectory { get; } = Resolve();

    public static string VueFile(string widgetName) =>
        Path.Combine(AssetsDirectory, widgetName + ".vue");

    private static string Resolve()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, RelativeAssetsPath);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{RelativeAssetsPath}' above '{AppContext.BaseDirectory}'."
        );
    }
}
