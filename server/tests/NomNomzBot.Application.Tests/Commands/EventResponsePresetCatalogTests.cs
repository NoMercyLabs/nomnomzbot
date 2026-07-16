// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// Proves the preset catalog is honest and self-consistent: every default template's placeholders are
/// variables that event ACTUALLY seeds (a pre-fill must never render a raw <c>{placeholder}</c> in chat),
/// event types are unique, and every preset ships a usable non-blank template. The seeding list is the
/// catalog's own key set, so the seeded rows and the presets cannot drift.
/// </summary>
public sealed partial class EventResponsePresetCatalogTests
{
    [Fact]
    public void Every_template_uses_only_variables_its_event_actually_seeds()
    {
        foreach (EventResponsePresetDto preset in EventResponsePresetCatalog.Presets)
        {
            IReadOnlyList<string> placeholders =
            [
                .. PlaceholderPattern()
                    .Matches(preset.DefaultTemplate)
                    .Select(m => m.Groups[1].Value),
            ];
            placeholders
                .Should()
                .BeSubsetOf(
                    preset.Variables,
                    "the {0} preset must never advertise a placeholder its trigger source won't fill",
                    preset.EventType
                );
        }
    }

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
            .OnlyContain(p => !string.IsNullOrWhiteSpace(p.DefaultTemplate))
            .And.OnlyContain(p => p.Variables.Count > 0);
    }

    [Fact]
    public void The_seeding_key_set_is_exactly_the_catalog()
    {
        EventResponsePresetCatalog
            .EventTypes.Should()
            .Equal(EventResponsePresetCatalog.Presets.Select(p => p.EventType));
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderPattern();
}
