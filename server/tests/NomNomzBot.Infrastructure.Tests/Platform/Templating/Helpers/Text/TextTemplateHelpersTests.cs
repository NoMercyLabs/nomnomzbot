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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

/// <summary>The text provider as the registry sees it: one namespace, merged rows, strings and seeded randomness.</summary>
public sealed class TextTemplateHelpersTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    [Fact]
    public void The_scan_registers_one_text_provider_with_every_row_of_the_lane()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("text.upper").Should().BeOfType<TextTemplateHelpers>();
        registry
            .ProviderFor("text.chars.shuffle")
            .Should()
            .BeSameAs(registry.ProviderFor("text.upper"));
        registry
            .All.Where(e => e.Key.StartsWith("text.", StringComparison.Ordinal))
            .Should()
            .HaveCount(48 + 55);
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        TextTemplateHelpers provider = new();
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
            entry.Sample.Should().NotBeNull(entry.Key);
            entry.Freshness.Should().Be(TemplateHelperFreshness.Pure);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(registry);

        foreach (
            TemplateHelperEntry entry in registry.All.Where(e =>
                e.Key.StartsWith("text.", StringComparison.Ordinal)
            )
        )
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
    public void A_seeded_random_gives_the_same_shuffle_every_time()
    {
        ShuffleTextHelpers first = new(new Random(1));
        ShuffleTextHelpers second = new(new Random(1));
        HelperNode words = new(
            "text.words.shuffle",
            ["a b c d e f"],
            "{text.words.shuffle:a b c d e f}"
        );
        HelperNode chars = new("text.chars.shuffle", ["abcdef"], "{text.chars.shuffle:abcdef}");
        HelperNode pick = new(
            "text.word.random",
            ["a b c d e f"],
            "{text.word.random:a b c d e f}"
        );

        string? shuffledWords = first.TryRender(words, null!);
        string? shuffledChars = first.TryRender(chars, null!);
        string? pickedWord = first.TryRender(pick, null!);

        second.TryRender(words, null!).Should().Be(shuffledWords);
        second.TryRender(chars, null!).Should().Be(shuffledChars);
        second.TryRender(pick, null!).Should().Be(pickedWord);
        shuffledWords!.Split(' ').Order().Should().Equal("a", "b", "c", "d", "e", "f");
        shuffledWords.Should().NotBe("a b c d e f");
        shuffledChars!.Order().Should().Equal("abcdef".ToCharArray());
        shuffledChars.Should().NotBe("abcdef");
    }
}
