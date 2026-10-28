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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Fun;

/// <summary>The fun provider as the registry sees it: one namespace, its rows, its strings and its samples.</summary>
public sealed class FunTemplateHelpersTests
{
    private const int RowCount = 26;

    [Fact]
    public async Task The_scan_registers_one_fun_provider_with_every_row_of_the_lane()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("fun.dice").Should().BeOfType<FunTemplateHelpers>();
        registry.ProviderFor("fun.ship").Should().BeSameAs(registry.ProviderFor("fun.dice"));
        registry
            .All.Where(e => e.Key.StartsWith("fun.", StringComparison.Ordinal))
            .Should()
            .HaveCount(RowCount);
    }

    [Fact]
    public async Task The_cut_size_row_is_not_built()
    {
        await using FunHelperFixture fixture = new();

        fixture.Registry.All.Select(e => e.Key).Should().NotContain("fun.size");
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using FunHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();

        foreach (TemplateHelperEntry entry in fixture.Provider.Entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBeNullOrWhiteSpace(entry.Key).And.NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrWhiteSpace(entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using FunHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in fixture.Provider.Entries)
        {
            string rendered = await fixture.RenderAsync(entry.Sample, FunHelperFixture.ChannelA);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}
