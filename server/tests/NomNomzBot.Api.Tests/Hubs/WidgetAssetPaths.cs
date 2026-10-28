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
