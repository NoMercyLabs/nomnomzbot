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

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The overlay host puts the accent in --nnz-accent: the widget's own color, else the channel's theme color, else
/// the theme's. A widget that paints with its own cfg.accentColor skips the channel's theme color, so none may
/// (2026-11-25: the audio and music player dots stayed purple under any theme color).
/// </summary>
public sealed class FirstPartyWidgetAccentTests
{
    private const string AssetPrefix = "NomNomzBot.Infrastructure.Content.Widgets.Assets.";

    private static readonly Regex PaintsWithOwnAccent = new(
        @":style=""[^""]*cfg\.accentColor",
        RegexOptions.Compiled
    );

    [Fact]
    public void No_first_party_widget_paints_with_its_own_accent_instead_of_the_theme_variable()
    {
        Assembly assembly = typeof(FirstPartyWidgetBundles).Assembly;
        List<string> offenders = [];
        foreach (
            string name in assembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith(AssetPrefix, StringComparison.Ordinal) && n.EndsWith(".vue")
                )
        )
        {
            using System.IO.Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            if (PaintsWithOwnAccent.IsMatch(reader.ReadToEnd()))
                offenders.Add(name[AssetPrefix.Length..]);
        }

        offenders.Should().BeEmpty();
    }
}
