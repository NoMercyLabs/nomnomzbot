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

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The SDK (<c>window.NomNomz</c>) owns every widget data read and holds the overlay token. A first-party widget
/// that calls <c>fetch(</c>, reads <c>WIDGET_TOKEN</c> or parses <c>location.search</c> goes around the SDK, so
/// the token leaks into widget code and the preview (which never touches the network) cannot stand in for it.
/// </summary>
public sealed partial class FirstPartyWidgetSdkOnlyDataAccessTests
{
    [GeneratedRegex(@"\bfetch\s*\(|WIDGET_TOKEN|location\.search")]
    private static partial Regex ForbiddenAccess();

    [Fact]
    public void No_first_party_widget_fetches_data_or_reads_the_overlay_token_itself()
    {
        string[] files = Directory.GetFiles(WidgetAssetPaths.AssetsDirectory);
        files
            .Should()
            .NotBeEmpty("the guard must read real widget sources, never pass on an empty folder");

        List<string> offenders =
        [
            .. files
                .OrderBy(f => f, StringComparer.Ordinal)
                .SelectMany(f =>
                    File.ReadAllLines(f)
                        .Select((line, index) => (line, index))
                        .Where(x => ForbiddenAccess().IsMatch(x.line))
                        .Select(x => $"{Path.GetFileName(f)}:{x.index + 1}: {x.line.Trim()}")
                ),
        ];

        offenders
            .Should()
            .BeEmpty(
                "widgets read data through NomNomz.data / NomNomz.spotify and never hold the token: "
                    + string.Join(" | ", offenders)
            );
    }
}
