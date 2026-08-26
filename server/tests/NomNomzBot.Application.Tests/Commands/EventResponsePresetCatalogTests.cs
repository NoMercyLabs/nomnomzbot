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
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// Proves the preset catalog is self-consistent: event types are unique, every preset publishes a translation
/// KEY derived from its own event type (never English prose — S-SCHEMA-I18N-redesign), and the seeding list is
/// the catalog's own key set, so the seeded rows and the presets cannot drift. The template TEXT itself lives in
/// the dashboard's strings.xml; that its placeholders are variables the event actually seeds is proven in
/// <c>EventResponsePresetTemplateTests</c> (Infrastructure.Tests/Localization), against the real en AND nl text.
/// </summary>
public sealed class EventResponsePresetCatalogTests
{
    [Fact]
    public void Event_types_are_unique_and_every_preset_has_a_usable_template()
    {
        EventResponsePresetCatalog
            .EventTypes.Should()
            .OnlyHaveUniqueItems()
            .And.NotBeEmpty()
            .And.OnlyContain(t => !string.IsNullOrWhiteSpace(t));
        EventResponsePresetCatalog
            .Presets.Should()
            .OnlyContain(p =>
                p.DefaultTemplate.Key == EventResponsePresetCatalog.TemplateKey(p.EventType)
            )
            .And.OnlyContain(p => p.Variables.Count > 0);
    }

    [Fact]
    public void The_seeding_key_set_is_exactly_the_catalog()
    {
        EventResponsePresetCatalog
            .EventTypes.Should()
            .Equal(EventResponsePresetCatalog.Presets.Select(p => p.EventType));
    }
}
