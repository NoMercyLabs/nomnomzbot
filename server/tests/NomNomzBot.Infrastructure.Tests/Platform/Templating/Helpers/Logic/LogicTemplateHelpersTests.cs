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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;
using NomNomzBot.Infrastructure.Tests.Localization;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>The logic providers as the registry sees them: ownership, strings and samples.</summary>
public sealed class LogicTemplateHelpersTests
{
    private static readonly Type[] ProviderTypes =
    [
        typeof(IfTemplateHelpers),
        typeof(DefaultTemplateHelpers),
        typeof(CoalesceTemplateHelpers),
        typeof(SwitchTemplateHelpers),
        typeof(ChanceTemplateHelpers),
        typeof(PickTemplateHelpers),
        typeof(RandomTemplateHelpers),
        typeof(LiteralTemplateHelpers),
        typeof(CharsTemplateHelpers),
    ];

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    private static IEnumerable<ITemplateHelperProvider> Providers() =>
        ProviderTypes.Select(t => (ITemplateHelperProvider)Activator.CreateInstance(t)!);

    [Fact]
    public void The_scan_registers_every_logic_provider_and_each_claims_its_keys()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        foreach (ITemplateHelperProvider provider in Providers())
        foreach (TemplateHelperEntry entry in provider.Entries.Where(e => e.Prefix is null))
            registry.ProviderFor(entry.Key).Should().BeOfType(provider.GetType(), entry.Key);

        registry.ProviderFor("chars.nothing").Should().BeOfType<CharsTemplateHelpers>();
        registry.ProviderFor("chars.zws").Should().BeOfType<CharsTemplateHelpers>();
        registry.ProviderFor("nothing").Should().BeNull("the short key is retired");
        registry.ProviderFor("zws").Should().BeNull("the short key is retired");
        registry.ProviderFor("if.stream.live").Should().BeOfType<IfTemplateHelpers>();
        registry.ProviderFor("if.args.2.matches").Should().BeOfType<IfTemplateHelpers>();
    }

    [Fact]
    public void The_as_built_random_keys_still_belong_to_the_as_built_helpers()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("random.number.100").Should().BeNull();
        registry.ProviderFor("random.number.1.6.2").Should().BeNull();
        registry.ProviderFor("random.pick.a.b.c").Should().BeNull();
        registry.ProviderFor("random.user").Should().BeNull();
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        DashboardStringsXmlCatalog strings = new();

        foreach (TemplateHelperEntry entry in Providers().SelectMany(p => p.Entries))
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNull(entry.Key);
            entry.Freshness.Should().Be(TemplateHelperFreshness.Pure, entry.Key);
        }
    }

    [Fact]
    public void Every_description_key_is_unique_across_the_logic_rows()
    {
        List<string> keys =
        [
            .. Providers().SelectMany(p => p.Entries).Select(e => e.Description.Key),
        ];

        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Every_sample_renders_to_itself_through_the_resolver()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(registry);

        foreach (TemplateHelperEntry entry in Providers().SelectMany(p => p.Entries))
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
    public void Only_the_rows_that_choose_among_their_arguments_are_lazy()
    {
        IEnumerable<TemplateHelperEntry> entries = Providers().SelectMany(p => p.Entries);

        entries
            .Where(e => e.LazyArguments)
            .Select(e => e.Key.Split('.')[0])
            .Distinct()
            .Should()
            .BeEquivalentTo("if", "default", "coalesce", "switch", "chance", "pick", "literal");
        entries
            .Where(e => e.Key is "pick.inline" or "pick")
            .Select(e => (e.Key, e.LazyArguments))
            .Should()
            .BeEquivalentTo([("pick", false), ("pick.inline", true)]);
    }
}
