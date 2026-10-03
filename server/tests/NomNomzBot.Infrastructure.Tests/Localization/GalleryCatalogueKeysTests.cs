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
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// S-EDITOR-I18N (gallery half): every first-party gallery entry carries a stable translation key pair derived
/// from its catalogue key, and both keys resolve to non-blank text in the English and Dutch dashboard files.
/// The keys are also part of the committed manifest (see SchemaLocalizationManifestTests).
/// </summary>
public sealed class GalleryCatalogueKeysTests
{
    [Fact]
    public void Every_catalogue_entry_has_a_name_and_description_key_derived_from_its_key()
    {
        FirstPartyWidgetCatalogue.All.Should().NotBeEmpty();

        foreach (FirstPartyWidgetDefinition widget in FirstPartyWidgetCatalogue.All)
        {
            widget.NameKey.Should().Be($"widget.gallery.{widget.Key}.name");
            widget.DescriptionKey.Should().Be($"widget.gallery.{widget.Key}.description");
        }
    }

    [Fact]
    public void Every_catalogue_key_has_non_blank_english_and_dutch_text()
    {
        DashboardStringsXmlCatalog catalog = new();

        foreach (FirstPartyWidgetDefinition widget in FirstPartyWidgetCatalogue.All)
        foreach (string key in new[] { widget.NameKey, widget.DescriptionKey })
        {
            catalog
                .TryGetEnglish(key, out string en)
                .Should()
                .BeTrue($"'{key}' needs an English entry in values/strings.xml");
            en.Should().NotBeNullOrWhiteSpace($"'{key}' needs non-blank English text");
            catalog
                .TryGetDutch(key, out string nl)
                .Should()
                .BeTrue($"'{key}' needs a Dutch entry in values-nl/strings.xml");
            nl.Should().NotBeNullOrWhiteSpace($"'{key}' needs non-blank Dutch text");
        }
    }
}
