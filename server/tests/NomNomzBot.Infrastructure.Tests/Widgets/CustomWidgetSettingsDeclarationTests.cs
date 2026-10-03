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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Tests.Localization;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The <c>settings.json</c> declaration of a custom widget: one valid file with every control type parses to the
/// exact fields, and every rule rejects with a message that names the field and the rule.
/// </summary>
public sealed class CustomWidgetSettingsDeclarationTests
{
    private const string AllTypes = """
        { "fields": [
          { "key": "enabled", "label": "Enabled", "type": "bool", "default": true, "group": "Basics", "help": "Turns it on" },
          { "key": "size", "label": "Size", "type": "number", "default": 12, "min": 1, "max": 40, "step": 0.5 },
          { "key": "title", "label": "Title", "type": "text", "default": "Hello" },
          { "key": "accent", "label": "Accent", "type": "color", "default": "#ff0066" },
          { "key": "mode", "label": "Mode", "type": "select", "default": "b",
            "options": [ { "value": "a", "label": "Alpha" }, { "value": "b", "label": "Beta" } ] },
          { "key": "tags", "label": "Tags", "type": "multiselect", "default": ["a"],
            "options": [ { "value": "a", "label": "Alpha" }, { "value": "b", "label": "Beta" } ] },
          { "key": "extra", "label": "Extra", "type": "json", "default": "{}" }
        ] }
        """;

    [Fact]
    public void Parse_ValidFileWithEveryType_ReturnsTheExactFields()
    {
        Result<IReadOnlyList<WidgetSettingsField>> result = CustomWidgetSettingsDeclaration.Parse(
            AllTypes
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        IReadOnlyList<WidgetSettingsField> fields = result.Value;
        fields
            .Select(f => (f.Key, f.Type))
            .Should()
            .Equal(
                ("enabled", "bool"),
                ("size", "number"),
                ("title", "text"),
                ("accent", "color"),
                ("mode", "select"),
                ("tags", "multiselect"),
                ("extra", "json")
            );

        fields[0].Default.Should().Be(true);
        fields[0].Label.Text.Should().Be("Enabled");
        fields[0].Group.Text.Should().Be("Basics");
        fields[0].Help!.Text.Should().Be("Turns it on");
        fields[1].Default.Should().Be(12d);
        fields[1].Min.Should().Be(1);
        fields[1].Max.Should().Be(40);
        fields[1].Step.Should().Be(0.5);
        fields[2].Default.Should().Be("Hello");
        fields[3].Default.Should().Be("#ff0066");
        fields[4].Default.Should().Be("b");
        fields[4]
            .Options!.Select(o => (o.Value, o.Label.Text))
            .Should()
            .Equal(("a", "Alpha"), ("b", "Beta"));
        ((IEnumerable<string>)fields[5].Default!).Should().Equal("a");
    }

    private static string One(string field) => "{ \"fields\": [ " + field + " ] }";

    public static TheoryData<string, string, string> Rejections =>
        new()
        {
            { "{ not json", "settings.json", "not valid JSON" },
            { "{ }", "settings.json", "fields" },
            {
                One("{ \"label\": \"X\", \"type\": \"text\", \"default\": \"\" }"),
                "field 0",
                "key"
            },
            {
                One(
                    "{ \"key\": \"1bad\", \"label\": \"X\", \"type\": \"text\", \"default\": \"\" }"
                ),
                "1bad",
                "letter"
            },
            {
                "{ \"fields\": [ { \"key\": \"a\", \"label\": \"X\", \"type\": \"text\", \"default\": \"\" }, { \"key\": \"a\", \"label\": \"Y\", \"type\": \"text\", \"default\": \"\" } ] }",
                "\"a\"",
                "twice"
            },
            {
                One("{ \"key\": \"a\", \"label\": \"X\", \"type\": \"slider\", \"default\": 1 }"),
                "\"a\"",
                "slider"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"select\", \"default\": \"x\" }"
                ),
                "\"a\"",
                "option"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"select\", \"default\": \"z\", \"options\": [ { \"value\": \"x\", \"label\": \"X\" } ] }"
                ),
                "\"a\"",
                "default"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"multiselect\", \"default\": [\"q\"], \"options\": [ { \"value\": \"x\", \"label\": \"X\" } ] }"
                ),
                "\"a\"",
                "default"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"bool\", \"default\": \"yes\" }"
                ),
                "\"a\"",
                "default"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"number\", \"default\": \"3\" }"
                ),
                "\"a\"",
                "default"
            },
            {
                One(
                    "{ \"key\": \"a\", \"label\": \"X\", \"type\": \"number\", \"default\": 1, \"min\": 5, \"max\": 2 }"
                ),
                "\"a\"",
                "min"
            },
        };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Parse_BrokenRule_FailsAndNamesTheFieldAndTheRule(
        string json,
        string namesField,
        string namesRule
    )
    {
        Result<IReadOnlyList<WidgetSettingsField>> result = CustomWidgetSettingsDeclaration.Parse(
            json
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_SETTINGS_INVALID");
        result.ErrorMessage.Should().Contain(namesField).And.Contain(namesRule);
    }

    [Fact]
    public void Parse_AuthorText_IsVerbatimAndTheDefaultGroupIsARealKey()
    {
        Result<IReadOnlyList<WidgetSettingsField>> result = CustomWidgetSettingsDeclaration.Parse(
            One(
                """
                { "key": "volume", "label": "volume", "type": "select", "default": "sound", "help": "How loud",
                  "options": [ { "value": "sound", "label": "sound" } ] }
                """
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        WidgetSettingsField field = result.Value.Single();
        field.Label.Key.Should().BeEmpty();
        field.Label.Text.Should().Be("volume");
        field.Help!.Key.Should().BeEmpty();
        field.Help.Text.Should().Be("How loud");
        field.Options!.Single().Label.Key.Should().BeEmpty();
        field.Options!.Single().Label.Text.Should().Be("sound");
        field.Group.Key.Should().Be("widget.custom.group.settings");
        field.Group.Text.Should().BeNull();
    }

    [Fact]
    public void DefaultGroupKey_HasAnEnglishAndADutchTranslation()
    {
        DashboardStringsXmlCatalog catalog = new();

        catalog
            .TryGetEnglish(CustomWidgetSettingsDeclaration.DefaultGroupKey, out string english)
            .Should()
            .BeTrue();
        english.Should().Be("Settings");
        catalog
            .TryGetDutch(CustomWidgetSettingsDeclaration.DefaultGroupKey, out string dutch)
            .Should()
            .BeTrue();
        dutch.Should().Be("Instellingen");
    }

    [Fact]
    public void Parse_MissingLabel_FallsBackToTheFieldKeyVerbatim()
    {
        Result<IReadOnlyList<WidgetSettingsField>> result = CustomWidgetSettingsDeclaration.Parse(
            One("""{ "key": "size", "type": "text", "default": "x", "group": "Look" }""")
        );

        WidgetSettingsField field = result.Value.Single();
        field.Label.Should().Be(LocalizedText.Verbatim("size"));
        field.Group.Should().Be(LocalizedText.Verbatim("Look"));
    }
}
