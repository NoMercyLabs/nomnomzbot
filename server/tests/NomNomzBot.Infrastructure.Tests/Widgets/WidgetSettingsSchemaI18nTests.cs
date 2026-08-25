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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// S-SCHEMA-I18N guard: every backend-authored, user-facing schema string on a first-party widget field must
/// carry BOTH an English and a Dutch translation. This walks the REAL schema returned by
/// <see cref="WidgetSettingsSchemaProvider.GetAll"/> — the same object graph the dashboard renders from — rather
/// than checking against a hand-maintained list of expected strings (this project has been burned by that kind
/// of guard before: it drifts the moment a field is added and nobody notices). A field whose <see cref="LocalizedText.Nl"/>
/// (or <see cref="LocalizedText.En"/>) is missing or blank fails loud here, at the same granularity a future
/// author would need to fix it (widget key + field key), instead of shipping an English-only control to a Dutch
/// dashboard.
/// </summary>
public sealed class WidgetSettingsSchemaI18nTests
{
    private static readonly WidgetSettingsSchemaProvider Provider = new();

    [Fact]
    public void Every_field_label_carries_both_english_and_dutch_translations()
    {
        foreach (WidgetSettingsSchema schema in Provider.GetAll())
        foreach (WidgetSettingsField field in schema.Fields)
        {
            AssertTranslated(field.Label, $"'{schema.WidgetKey}.{field.Key}' label");
        }
    }

    [Fact]
    public void Every_field_help_text_that_exists_carries_both_english_and_dutch_translations()
    {
        foreach (WidgetSettingsSchema schema in Provider.GetAll())
        foreach (WidgetSettingsField field in schema.Fields)
        {
            if (field.Help is not null)
                AssertTranslated(field.Help, $"'{schema.WidgetKey}.{field.Key}' help text");
        }
    }

    private static void AssertTranslated(LocalizedText text, string what)
    {
        text.Key.Should().NotBeNullOrWhiteSpace($"{what} needs a translation key");
        text.En.Should().NotBeNullOrWhiteSpace($"{what} needs an English translation");
        text.Nl.Should()
            .NotBeNullOrWhiteSpace(
                $"{what} (key '{text.Key}') needs a Dutch translation — add it to "
                    + "WidgetSettingsSchemaProvider.NlTranslations"
            );
    }
}
