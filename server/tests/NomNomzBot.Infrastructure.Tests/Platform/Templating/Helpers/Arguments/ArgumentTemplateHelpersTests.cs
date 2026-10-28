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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;
using NomNomzBot.Infrastructure.Tests.Localization;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Arguments;

/// <summary>The args provider as the registry sees it: one namespace, merged rows, strings, and samples.</summary>
public sealed class ArgumentTemplateHelpersTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    [Fact]
    public void The_scan_registers_one_args_provider_for_the_new_keys_and_leaves_the_seed_keys_to_the_legacy_code()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("args.rest").Should().BeOfType<ArgumentTemplateHelpers>();
        registry.ProviderFor("args.2+").Should().BeSameAs(registry.ProviderFor("args.rest"));
        registry.ProviderFor("args.2-3").Should().BeSameAs(registry.ProviderFor("args.rest"));
        registry.ProviderFor("args.2.upper").Should().BeSameAs(registry.ProviderFor("args.rest"));
        registry.ProviderFor("args.5").Should().BeSameAs(registry.ProviderFor("args.rest"));
        registry.ProviderFor("args").Should().BeNull();
        new ArgumentTemplateHelpers().Entries.Should().HaveCount(21);
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        ArgumentTemplateHelpers provider = new();
        DashboardStringsXmlCatalog strings = new();

        foreach (TemplateHelperEntry entry in provider.Entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Freshness.Should().Be(TemplateHelperFreshness.Seed);
            entry
                .Contexts.Should()
                .BeEquivalentTo([TemplateHelperContext.Command, TemplateHelperContext.Pipeline]);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using ServiceProvider services = BuildServices();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(
            services.GetRequiredService<ITemplateHelperRegistry>()
        );

        foreach (TemplateHelperEntry entry in new ArgumentTemplateHelpers().Entries)
        {
            string rendered = await resolver.ResolveAsync(
                entry.Sample,
                new Dictionary<string, string>(),
                broadcasterId: null
            );

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public void A_shaped_row_claims_the_args_prefix_and_a_fixed_row_claims_only_its_key()
    {
        ArgumentTemplateHelpers provider = new();

        provider
            .Entries.Where(e => e.Prefix is not null)
            .Select(e => e.Key)
            .Should()
            .BeEquivalentTo(
                "args.<n>+",
                "args.<a>-<b>",
                "args.<n>:<default>",
                "args.<n>.clean",
                "args.<n>.upper",
                "args.<n>.length",
                "args.<n>.urlEncoded",
                "args.<n>.number",
                "args.<n>.isNumber",
                "args.<n>.isMention"
            );
        provider
            .Entries.Where(e => e.Prefix is null)
            .Select(e => e.Key)
            .Should()
            .OnlyContain(k => k.StartsWith("args.", StringComparison.Ordinal) && !k.Contains('<'));
    }
}
