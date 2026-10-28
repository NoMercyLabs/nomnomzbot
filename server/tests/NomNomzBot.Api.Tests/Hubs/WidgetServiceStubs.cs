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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Widget service substitutes for hub tests that do not test the settings a joining page receives.</summary>
internal static class WidgetServiceStubs
{
    /// <summary>The effective-settings lookup fails, so a joining page gets the widget's saved settings bag.</summary>
    public static IWidgetService SavedSettingsOnly()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetEffectiveSettingsAsync(default, default)
            .ReturnsForAnyArgs(
                Result.Failure<Dictionary<string, object>>("not under test", "NOT_UNDER_TEST")
            );
        return widgets;
    }
}
