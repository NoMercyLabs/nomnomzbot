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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The drift guard for the template helper hint of the SDK types: <c>TemplateHelperName</c> (the union of every
/// registry key) and <c>NnzTemplateHelperMap</c> (key to one-line description) are written from the registry,
/// so a new registry row reaches both contexts without a hand edit. These tests read the registry the running
/// bot builds (the static rows plus every provider) and fail with the missing row named.
/// </summary>
public sealed partial class SdkTemplateHelperTypesTests
{
    private static ITemplateHelperRegistry Registry()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return services.GetRequiredService<ITemplateHelperRegistry>();
    }

    private static string Dts(SdkContext context) =>
        new SdkTypeEmitter(new EventCatalog(), helpers: Registry()).EmitTypeScript(context);

    [GeneratedRegex(@"type TemplateHelperName =([^;]*);", RegexOptions.Singleline)]
    private static partial Regex UnionPattern();

    [GeneratedRegex(@"interface NnzTemplateHelperMap \{(.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex MapPattern();

    [Theory]
    [InlineData(SdkContext.Script)]
    [InlineData(SdkContext.Widget)]
    public void Every_registry_row_is_in_the_helper_name_union(SdkContext context)
    {
        string dts = Dts(context);
        Match union = UnionPattern().Match(dts);
        union.Success.Should().BeTrue("the types must declare TemplateHelperName");

        List<string> missing =
        [
            .. Registry()
                .All.Select(entry => entry.Key)
                .Where(key =>
                    !union.Groups[1].Value.Contains($"'{key}'", StringComparison.Ordinal)
                ),
        ];

        missing
            .Should()
            .BeEmpty(
                "TemplateHelperName misses these registry rows: " + string.Join(", ", missing)
            );
    }

    [Theory]
    [InlineData(SdkContext.Script)]
    [InlineData(SdkContext.Widget)]
    public void Every_registry_row_has_a_one_line_description_in_the_map(SdkContext context)
    {
        string dts = Dts(context);
        Match map = MapPattern().Match(dts);
        map.Success.Should().BeTrue("the types must declare NnzTemplateHelperMap");

        List<string> missing =
        [
            .. Registry()
                .All.Select(entry => entry.Key)
                .Where(key =>
                    !Regex.IsMatch(
                        map.Groups[1].Value,
                        $@"^\s+'{Regex.Escape(key)}': '.+';\r?$",
                        RegexOptions.Multiline
                    )
                ),
        ];

        missing
            .Should()
            .BeEmpty("NnzTemplateHelperMap has no description for: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_row_carries_its_English_description_from_the_translation_file()
    {
        string dts = Dts(SdkContext.Script);

        dts.Should().Contain("  'text.upper': 'Makes the text UPPERCASE';");
    }

    [Fact]
    public void The_render_call_is_declared_for_both_contexts_with_one_typed_signature()
    {
        Dts(SdkContext.Script)
            .Should()
            .Contain(
                "render(template: string, values?: Record<string, string | number | boolean>): string;"
            );
        Dts(SdkContext.Widget)
            .Should()
            .Contain(
                "render(template: string, values?: Record<string, string | number | boolean>): Promise<string>;"
            );
    }
}
