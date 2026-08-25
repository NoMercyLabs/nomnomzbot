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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// `socials.vue`'s `normalize()` parses each handle entry as <c>{ label, handle }</c> (it reads <c>h.handle</c>,
/// drops any entry with a blank handle) — but the settings-schema help text used to document the shape as
/// <c>{ label, url }</c> (widget-quality-audit §1), so a broadcaster who typed exactly what the field told them
/// to got a permanently empty rotation.
///
/// `normalize()` is pure TypeScript inside a `.vue` `&lt;script setup&gt;` block, compiled by esbuild/Vue SFC at
/// widget-build time — there is no xUnit-reachable CLR entry point for it (extracting it to a shared, unit-testable
/// module is out of scope for this fix; the systemic bug in scope here is the field-name/help-text contract, not a
/// widget-editor architecture change). So this test proves the two things ARE consistent by construction, tied to
/// the real files, rather than asserting a static string in isolation: (1) it reads the real `normalize()` source
/// and extracts which property name it actually keys the handle on, and (2) it reads the real schema help text and
/// asserts that same property name is documented — and that the old, wrong one is not. A future edit that changes
/// either side without the other breaks this test.
/// </summary>
public sealed class SocialsWidgetHelpTextTests
{
    private static readonly WidgetSettingsSchemaProvider Provider = new();

    [Fact]
    public void Help_text_documents_the_field_the_parser_actually_reads()
    {
        string vueSource = File.ReadAllText(WidgetAssetPaths.VueFile("socials"));

        // Extract normalize()'s body and find the property it reads off each raw entry for the rotation text
        // (`(h && h.<prop>)`), distinct from `label` which the parser also reads but is not in dispute.
        string normalizeBody = ExtractFunctionBody(vueSource, "normalize");
        Match handleKeyMatch = Regex.Match(
            normalizeBody,
            @"handle:\s*String\(\(h\s*&&\s*h\.(\w+)\)"
        );
        handleKeyMatch
            .Success.Should()
            .BeTrue(
                "normalize() should read the handle off a `handle: String((h && h.<prop>) ...)` pattern"
            );
        string handleProperty = handleKeyMatch.Groups[1].Value;
        handleProperty
            .Should()
            .Be("handle", "the parser's own field name is the ground truth for this test");

        WidgetSettingsSchema? schema = Provider.GetByKey("socials");
        schema.Should().NotBeNull();
        WidgetSettingsField handlesField = schema
            .Fields.Should()
            .ContainSingle(f => f.Key == "handles")
            .Subject;

        DashboardStringsXmlCatalog catalog = new();
        catalog
            .TryGetEnglish(handlesField.Help!.Key, out string helpEnglish)
            .Should()
            .BeTrue(
                $"'{handlesField.Help.Key}' should have an English entry in values/strings.xml"
            );

        helpEnglish
            .Should()
            .Contain(
                handleProperty,
                "the help text must document the property the parser actually reads"
            );
        helpEnglish
            .Should()
            .NotContain(
                "label + url",
                "the old help text claimed a `url` field the parser never reads"
            );
    }

    // Brace-counting extraction (regex alone can't safely match nested braces): finds "function <name>(" then
    // returns everything up to the matching closing brace.
    private static string ExtractFunctionBody(string source, string functionName)
    {
        string marker = "function " + functionName + "(";
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"'{marker}' should exist in the widget source");
        int braceOpen = source.IndexOf('{', start);
        int depth = 0;
        int i = braceOpen;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }
        return source[braceOpen..(i + 1)];
    }
}
