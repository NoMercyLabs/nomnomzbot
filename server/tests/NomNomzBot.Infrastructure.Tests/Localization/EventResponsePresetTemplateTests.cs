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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// The event-response preset templates moved out of C# into the dashboard's translation files, so the honesty
/// guarantee moved with them: every placeholder in the REAL English AND Dutch default template must be a
/// variable that event's trigger source actually seeds (a pre-fill must never render a raw
/// <c>{placeholder}</c> in chat), in both languages. A translator who invents <c>{viewer}</c> where the event
/// only seeds <c>{user}</c> fails here, not in the streamer's chat.
/// </summary>
public sealed class EventResponsePresetTemplateTests
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

            TemplateTree
                .StaticPlaceholders(TemplateParser.Parse(english))
                .Select(token => token.StaticContent)
                .Should()
                .BeSubsetOf(
                    preset.Variables,
                    "the {0} English preset must never advertise a placeholder its trigger source won't fill",
                    preset.EventType
                );
            TemplateTree
                .StaticPlaceholders(TemplateParser.Parse(dutch))
                .Select(token => token.StaticContent)
                .Should()
                .BeSubsetOf(
                    preset.Variables,
                    "the {0} Dutch preset must never advertise a placeholder its trigger source won't fill",
                    preset.EventType
                );
        }
    }
}
