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
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Numbers;

/// <summary>The three number providers as the registry sees them: namespaces, strings and samples.</summary>
public sealed class NumberTemplateHelpersTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    private static bool IsNumberKey(TemplateHelperEntry entry) =>
        entry.Key is "math" or "meter"
        || entry.Key.StartsWith("math.", StringComparison.Ordinal)
        || entry.Key.StartsWith("meter.", StringComparison.Ordinal)
        || entry.Key.StartsWith("num.", StringComparison.Ordinal);

    [Fact]
    public void The_scan_registers_one_provider_per_namespace_with_every_row_of_the_lane()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("math").Should().BeSameAs(registry.ProviderFor("math.median"));
        registry.ProviderFor("meter").Should().BeSameAs(registry.ProviderFor("meter.stars"));
        registry.ProviderFor("num.min").Should().BeSameAs(registry.ProviderFor("num.bar"));
        registry.ProviderFor("math").Should().NotBeSameAs(registry.ProviderFor("num.min"));
        registry.ProviderFor("meter").Should().NotBeSameAs(registry.ProviderFor("num.min"));
        registry
            .All.Where(e => e.Key.StartsWith("math", StringComparison.Ordinal))
            .Should()
            .HaveCount(16);
        registry
            .All.Where(e => e.Key.StartsWith("num.", StringComparison.Ordinal))
            .Should()
            .HaveCount(36);
        registry
            .All.Where(e => e.Key.StartsWith("meter", StringComparison.Ordinal))
            .Should()
            .HaveCount(4);
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        DashboardStringsXmlCatalog strings = new();

        foreach (TemplateHelperEntry entry in registry.All.Where(IsNumberKey))
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Freshness.Should().Be(TemplateHelperFreshness.Pure, entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        TemplateResolver resolver = NumberHelpersTableTests.BuildResolver(registry);

        foreach (TemplateHelperEntry entry in registry.All.Where(IsNumberKey))
        {
            string rendered = await resolver.ResolveAsync(
                entry.Sample,
                new Dictionary<string, string>(),
                broadcasterId: null
            );

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}
