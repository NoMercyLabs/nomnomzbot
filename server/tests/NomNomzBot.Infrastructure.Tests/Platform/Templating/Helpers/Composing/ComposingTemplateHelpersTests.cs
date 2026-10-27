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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chatter;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Quotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Variables;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Composing;

/// <summary>The composing providers as the registry sees them: ownership, strings and samples.</summary>
public sealed class ComposingTemplateHelpersTests
{
    private static readonly string[] OwnedKeys =
    [
        "quote",
        "quote.text",
        "quote.author",
        "quote.game",
        "quote.date",
        "quote.count",
        "quote.latest",
        "random.quote",
        "random.chatter",
        "random.chatter.notMe",
        "random.chatter.mention",
        "set",
        "include.command",
        "include.list",
    ];

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    private static IEnumerable<TemplateHelperEntry> OwnEntries(ITemplateHelperRegistry registry) =>
        registry.All.Where(e =>
            registry.ProviderFor(e.Prefix is null ? e.Key : e.Prefix + "x")
                is ChatterTemplateHelpers
                    or QuoteTemplateHelpers
                    or VariableTemplateHelpers
                    or IncludeTemplateHelpers
        );

    [Fact]
    public void The_scan_registers_every_composing_provider_and_each_claims_its_keys()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("quote.text").Should().BeOfType<QuoteTemplateHelpers>();
        registry.ProviderFor("random.quote").Should().BeOfType<QuoteTemplateHelpers>();
        registry.ProviderFor("random.chatter").Should().BeOfType<ChatterTemplateHelpers>();
        registry.ProviderFor("random.chatter.notMe").Should().BeOfType<ChatterTemplateHelpers>();
        registry.ProviderFor("set").Should().BeOfType<VariableTemplateHelpers>();
        registry.ProviderFor("var.anything").Should().BeOfType<VariableTemplateHelpers>();
        registry.ProviderFor("include.command").Should().BeOfType<IncludeTemplateHelpers>();
        registry.ProviderFor("command.hug").Should().BeOfType<IncludeTemplateHelpers>();
        registry.ProviderFor("include.list").Should().BeOfType<IncludeTemplateHelpers>();
        OwnedKeys.Should().OnlyContain(k => registry.Find(k) != null);
    }

    [Fact]
    public void The_as_built_and_logic_random_keys_keep_their_owners()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("random.user").Should().BeNull();
        registry.ProviderFor("random.pick.a.b").Should().BeNull();
        registry.ProviderFor("random.number").Should().NotBeOfType<ChatterTemplateHelpers>();
        registry.ProviderFor("random.color").Should().NotBeOfType<QuoteTemplateHelpers>();
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = [.. OwnEntries(registry)];

        entries.Should().HaveCountGreaterThanOrEqualTo(OwnedKeys.Length);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNull(entry.Key);
        }
        entries.Select(e => e.Description.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Every_sample_renders_to_itself_through_the_resolver()
    {
        await using ComposingHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in OwnEntries(fixture.Registry))
            (await fixture.RenderAsync(entry.Sample, null)).Should().Be(entry.Sample, entry.Key);
    }
}
