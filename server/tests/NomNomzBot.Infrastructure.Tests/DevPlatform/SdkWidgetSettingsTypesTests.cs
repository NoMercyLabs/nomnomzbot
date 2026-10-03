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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// A widget's own settings are typed from its settings schema: <c>NomNomz.settings</c>, <c>onSettings</c> and
/// <c>WIDGET_SETTINGS</c> carry one <c>NnzWidgetSettings</c> interface, so a misspelled setting name is a compile
/// error in the editor instead of a silent <c>undefined</c> on stream.
/// </summary>
public sealed partial class SdkWidgetSettingsTypesTests
{
    private static LocalizedText Text(string key) => new(key);

    private static WidgetSettingsSchema Schema() =>
        new(
            "demo",
            "Demo",
            [
                new WidgetSettingsField(
                    "durationMs",
                    new LocalizedText("demo.duration.label", "How long it shows"),
                    "number",
                    Text("demo.group"),
                    4000,
                    new LocalizedText("demo.duration.help", "In milliseconds."),
                    Min: 100,
                    Max: 60000
                ),
                new WidgetSettingsField(
                    "showName",
                    Text("demo.showname.label"),
                    "bool",
                    Text("demo.group"),
                    true
                ),
                new WidgetSettingsField(
                    "layout",
                    Text("demo.layout.label"),
                    "select",
                    Text("demo.group"),
                    "row",
                    Options:
                    [
                        new WidgetSettingsFieldOption("row", Text("demo.layout.row")),
                        new WidgetSettingsFieldOption("column", Text("demo.layout.column")),
                    ]
                ),
                new WidgetSettingsField(
                    "events",
                    Text("demo.events.label"),
                    "multiselect",
                    Text("demo.group"),
                    null,
                    Options:
                    [
                        new WidgetSettingsFieldOption("follow", Text("demo.events.follow")),
                        new WidgetSettingsFieldOption("raid", Text("demo.events.raid")),
                    ]
                ),
                new WidgetSettingsField(
                    "accent",
                    Text("demo.accent.label"),
                    "color",
                    Text("demo.group"),
                    "#ff0000"
                ),
                new WidgetSettingsField(
                    "title",
                    Text("demo.title.label"),
                    "text",
                    Text("demo.group"),
                    ""
                ),
                new WidgetSettingsField(
                    "extra",
                    Text("demo.extra.label"),
                    "json",
                    Text("demo.group"),
                    null
                ),
                new WidgetSettingsField(
                    "mystery",
                    Text("demo.mystery.label"),
                    "hologram",
                    Text("demo.group"),
                    null
                ),
            ],
            []
        );

    private static string Dts(WidgetSettingsSchema schema) =>
        new SdkTypeEmitter(new EventCatalog())
            .EmitWidgetTypeScript(schema)
            .ReplaceLineEndings("\n");

    private static string SettingsInterface(string dts)
    {
        Match match = SettingsBlock().Match(dts);
        match.Success.Should().BeTrue("the declaration must carry interface NnzWidgetSettings");
        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"interface NnzWidgetSettings \{\r?\n(.*?)\r?\n\}", RegexOptions.Singleline)]
    private static partial Regex SettingsBlock();

    [GeneratedRegex(@"\bany\b")]
    private static partial Regex AnyKeyword();

    [Fact]
    public void Each_field_type_maps_to_its_typescript_type()
    {
        string body = SettingsInterface(Dts(Schema()));

        body.Should().Contain("  durationMs: number;");
        body.Should().Contain("  showName: boolean;");
        body.Should().Contain("  layout: 'row' | 'column';");
        body.Should().Contain("  events: ('follow' | 'raid')[];");
        body.Should().Contain("  accent: string;");
        body.Should().Contain("  title: string;");
        body.Should().Contain("  extra: unknown;");
        body.Should().Contain("  mystery: unknown;");
    }

    [Fact]
    public void A_member_carries_its_label_and_help_as_jsdoc()
    {
        string body = SettingsInterface(Dts(Schema()));

        body.Should().Contain("/** How long it shows. In milliseconds. */\n  durationMs: number;");
    }

    [Fact]
    public void A_member_with_only_translation_keys_carries_no_jsdoc()
    {
        string body = SettingsInterface(Dts(Schema()));

        body.Should().Contain("\n  showName: boolean;");
        body.Should().NotContain("demo.showname.label");
        body.Should().NotContain("demo.group");
    }

    [Fact]
    public void Settings_onSettings_and_WIDGET_SETTINGS_use_the_settings_interface()
    {
        string dts = Dts(Schema());

        dts.Should().Contain("(settings: NnzWidgetSettings) => void): NnzOverlaySdk;");
        dts.Should().Contain("  readonly settings: NnzWidgetSettings;");
        dts.Should().Contain("declare const WIDGET_SETTINGS: NnzWidgetSettings;");
        dts.Should().NotContain("Record<string, unknown>;\n  readonly actions");
    }

    [Fact]
    public void Without_a_widget_the_settings_stay_an_open_record_and_no_interface_is_declared()
    {
        string dts = new SdkTypeEmitter(new EventCatalog()).EmitTypeScript(SdkContext.Widget);

        dts.Should().NotContain("interface NnzWidgetSettings");
        dts.Should().Contain("  readonly settings: Record<string, unknown>;");
        dts.Should().Contain("declare const WIDGET_SETTINGS: Record<string, unknown>;");
    }

    [Fact]
    public void The_typed_widget_surface_declares_no_any()
    {
        string dts = Dts(Schema());

        List<string> offenders =
        [
            .. dts.Split('\n')
                .Select(line => line.Trim())
                .Where(line =>
                    line.Length > 0
                    && !line.StartsWith("//", StringComparison.Ordinal)
                    && !line.StartsWith("/*", StringComparison.Ordinal)
                    && !line.StartsWith('*')
                    && AnyKeyword().IsMatch(line)
                ),
        ];

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    public void A_key_that_is_not_an_identifier_is_quoted()
    {
        WidgetSettingsSchema schema = new(
            "demo",
            "Demo",
            [
                new WidgetSettingsField(
                    "bar-color",
                    Text("demo.bar.label"),
                    "text",
                    Text("demo.group"),
                    ""
                ),
            ],
            []
        );

        SettingsInterface(Dts(schema)).Should().Contain("  'bar-color': string;");
    }

    [Fact]
    public void A_select_option_with_a_quote_is_escaped()
    {
        WidgetSettingsSchema schema = new(
            "demo",
            "Demo",
            [
                new WidgetSettingsField(
                    "mode",
                    Text("demo.mode.label"),
                    "select",
                    Text("demo.group"),
                    "it's",
                    Options: [new WidgetSettingsFieldOption("it's", Text("demo.mode.x"))]
                ),
            ],
            []
        );

        SettingsInterface(Dts(schema)).Should().Contain("  mode: 'it\\'s';");
    }
}
