// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A name in a recap list row is clipped to one line (<c>overflow: hidden</c>). The clip box is one line tall, so
/// a line height near 1 cuts the descenders of letters like g, j and y ("GwakkaMolay", "JDDoesDev"). The name
/// must carry a line height tall enough to hold them.
/// </summary>
public sealed class RaidRecapRowLineHeightTests
{
    private const double MinimumLineHeight = 1.25;

    private static string LoadAsset()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        const string name = "NomNomzBot.Infrastructure.Content.Widgets.Assets.raid_recap.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(name);
        stream.Should().NotBeNull($"the embedded asset '{name}' should exist");
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_clipped_name_in_a_list_row_has_a_line_height_that_holds_descenders()
    {
        string css = LoadAsset();

        Match rule = Regex.Match(css, @"\.row \.n\s*\{(?<body>[^}]*)\}");
        rule.Success.Should().BeTrue("the row name rule must exist");
        rule.Groups["body"].Value.Should().Contain("overflow: hidden");

        Match height = Regex.Match(rule.Groups["body"].Value, @"line-height:\s*(?<n>[\d.]+)\s*;");
        height
            .Success.Should()
            .BeTrue("a clipped name must set its own line height, not inherit a tight one");
        double value = double.Parse(height.Groups["n"].Value, CultureInfo.InvariantCulture);
        value.Should().BeGreaterThanOrEqualTo(MinimumLineHeight);
    }
}
