// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
