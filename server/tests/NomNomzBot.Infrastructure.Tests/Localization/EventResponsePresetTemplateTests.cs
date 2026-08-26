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

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// The event-response preset templates moved out of C# into the dashboard's translation files, so the honesty
/// guarantee moved with them: every placeholder in the REAL English AND Dutch default template must be a
/// variable that event's trigger source actually seeds (a pre-fill must never render a raw
/// <c>{placeholder}</c> in chat), in both languages. A translator who invents <c>{viewer}</c> where the event
/// only seeds <c>{user}</c> fails here, not in the streamer's chat.
/// </summary>
public sealed partial class EventResponsePresetTemplateTests
{
    [Fact]
    public void Every_english_and_dutch_template_uses_only_variables_its_event_actually_seeds()
    {
        DashboardStringsXmlCatalog catalog = new();

        foreach (EventResponsePresetDto preset in EventResponsePresetCatalog.Presets)
        {
            catalog
                .TryGetEnglish(preset.DefaultTemplate.Key, out string english)
                .Should()
                .BeTrue($"'{preset.EventType}' needs an English default template");
            catalog
                .TryGetDutch(preset.DefaultTemplate.Key, out string dutch)
                .Should()
                .BeTrue($"'{preset.EventType}' needs a Dutch default template");

            PlaceholdersIn(english)
                .Should()
                .BeSubsetOf(
                    preset.Variables,
                    "the {0} English preset must never advertise a placeholder its trigger source won't fill",
                    preset.EventType
                );
            PlaceholdersIn(dutch)
                .Should()
                .BeSubsetOf(
                    preset.Variables,
                    "the {0} Dutch preset must never advertise a placeholder its trigger source won't fill",
                    preset.EventType
                );
        }
    }

    private static IReadOnlyList<string> PlaceholdersIn(string template) =>
        [.. PlaceholderPattern().Matches(template).Select(match => match.Groups[1].Value)];

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderPattern();
}
