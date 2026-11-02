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

using FluentAssertions;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The stream recap is a general scrolling credits list by default. Stoney's own look stays available as settings of
/// the same widget. Every setting that picks between the two looks must have a default and a plain title.
/// </summary>
public sealed class RaidRecapLayoutSettingsTests
{
    private static readonly WidgetSettingsSchema Schema =
        new WidgetSettingsSchemaProvider().GetByKey("raid_recap")!;

    private static FirstPartyWidgetDefinition Recap =>
        FirstPartyWidgetCatalogue.All.Single(widget => widget.Key == "raid_recap");

    public static TheoryData<string, object> NewSettingDefaults =>
        new()
        {
            { "layout", "credits" },
            { "hueByDay", false },
            { "timeZone", "" },
            { "rollSeconds", 40 },
            { "roll", true },
        };

    [Theory]
    [MemberData(nameof(NewSettingDefaults))]
    public void Each_new_setting_has_its_default_in_the_catalogue_and_in_the_schema(
        string key,
        object expected
    )
    {
        Recap.DefaultSettings[key].Should().Be(expected);

        WidgetSettingsField field = Schema.Fields.Single(f => f.Key == key);
        field.Default.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(NewSettingDefaults))]
    public void Each_new_setting_has_a_plain_english_title(string key, object _)
    {
        WidgetSettingsField field = Schema.Fields.Single(f => f.Key == key);
        DashboardStringsXmlCatalog catalog = new();

        catalog.TryGetEnglish(field.Label.Key, out string title).Should().BeTrue();
        title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_roll_speed_is_limited_to_ten_through_three_hundred_seconds()
    {
        WidgetSettingsField field = Schema.Fields.Single(f => f.Key == "rollSeconds");

        field.Min.Should().Be(10);
        field.Max.Should().Be(300);
    }

    [Fact]
    public void The_layout_offers_credits_and_grid_and_names_both()
    {
        WidgetSettingsField field = Schema.Fields.Single(f => f.Key == "layout");
        DashboardStringsXmlCatalog catalog = new();

        field.Options!.Select(o => o.Value).Should().Equal("credits", "grid");
        foreach (WidgetSettingsFieldOption option in field.Options!)
        {
            catalog.TryGetEnglish(option.Label.Key, out string name).Should().BeTrue();
            name.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_default_thanks_line_is_general_and_keeps_the_duration_placeholder()
    {
        Dictionary<string, object> labels =
            (Dictionary<string, object>)Recap.DefaultSettings["labels"];

        labels["thanks"].Should().Be("Thanks for watching! You spent {duration} with us.");
    }
}
