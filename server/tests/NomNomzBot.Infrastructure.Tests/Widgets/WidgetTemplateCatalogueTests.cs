// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the starter templates are real, distinct, SDK-using widgets — not empty placeholders — so "create a
/// widget" always yields a working starting point.
/// </summary>
public sealed class WidgetTemplateCatalogueTests
{
    [Fact]
    public void Every_template_is_a_complete_sdk_using_vanilla_widget_with_a_unique_key()
    {
        IReadOnlyList<WidgetTemplate> all = WidgetTemplateCatalogue.All;

        all.Should().NotBeEmpty();
        all.Select(t => t.Key).Should().OnlyHaveUniqueItems();
        all.Should()
            .OnlyContain(t =>
                !string.IsNullOrWhiteSpace(t.Key)
                && !string.IsNullOrWhiteSpace(t.Name)
                && !string.IsNullOrWhiteSpace(t.Description)
                && t.Framework == "vanilla"
                && t.Source.Contains("<script>") // a real document
                && t.Source.Contains("NomNomz.") // uses the overlay SDK
            );
    }

    [Fact]
    public void The_alert_template_wires_the_core_channel_events()
    {
        WidgetTemplate alerts = WidgetTemplateCatalogue.All.Single(t => t.Key == "alerts");

        alerts
            .Source.Should()
            .Contain("NomNomz.on('follow'")
            .And.Contain("NomNomz.on('subscription'")
            .And.Contain("NomNomz.on('cheer'")
            .And.Contain("NomNomz.on('raid'");
    }
}
