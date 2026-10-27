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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Emotes;

public sealed class EmoteTemplateHelpersTests
{
    private static readonly string[] ChannelPool =
    [
        "sampleHype",
        "sampleLove",
        "sampleWave",
        "sampleBits",
    ];

    public static TheoryData<string, string[]> RandomRows =>
        new()
        {
            { "{emote.random}", ChannelPool },
            { "{emote.random.sub}", ["sampleHype", "sampleLove"] },
            { "{emote.random.follower}", ["sampleWave"] },
            { "{emote.random.bits}", ["sampleBits"] },
            { "{emote.random.global}", ["Kappa", "LUL"] },
            { "{emote.random.7tv}", ["catJAM", "KEKW", "PepeLaugh"] },
            { "{emote.random.bttv}", ["peepoHappy", "widepeepo"] },
            { "{emote.random.ffz}", ["monkaS"] },
            {
                "{emote.random.any}",
                [
                    .. ChannelPool,
                    "Kappa",
                    "LUL",
                    "catJAM",
                    "KEKW",
                    "PepeLaugh",
                    "Clap",
                    "peepoHappy",
                    "widepeepo",
                    "monkaS",
                    "ZreknarF",
                ]
            },
        };

    [Theory]
    [MemberData(nameof(RandomRows))]
    public async Task A_random_row_draws_once_from_the_injected_random_over_its_pool(
        string template,
        string[] pool
    )
    {
        await using EmoteHelperFixture fixture = new(seed: 1);

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(pool[new Random(1).Next(pool.Length)]);
    }

    [Fact]
    public async Task Two_random_tokens_in_one_render_draw_in_turn_from_the_one_random()
    {
        await using EmoteHelperFixture fixture = new(seed: 1);
        Random reference = new(1);
        string first = ChannelPool[reference.Next(ChannelPool.Length)];
        string second = ChannelPool[reference.Next(ChannelPool.Length)];

        string rendered = await fixture.RenderAsync("{emote.random} {emote.random}");

        rendered.Should().Be($"{first} {second}");
    }

    [Fact]
    public async Task The_scan_registers_one_emote_provider_that_owns_the_emote_keys()
    {
        await using EmoteHelperFixture fixture = new();

        ITemplateHelperProvider? provider = fixture.Registry.ProviderFor("emote.random.sub");

        provider.Should().NotBeNull();
        provider!.Namespace.Should().Be("emote");
        fixture.Registry.ProviderFor("emote").Should().BeSameAs(provider);
        fixture.Registry.ProviderFor("emote.cheermote").Should().BeSameAs(provider);
        fixture.Registry.ProviderFor("emote.nope").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using EmoteHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = EmoteEntries(fixture);

        entries.Should().HaveCount(30);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using EmoteHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in EmoteEntries(fixture))
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task Many_emote_tokens_in_one_render_read_each_source_once()
    {
        await using EmoteHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{emote.list.channel} {emote.count.channel} {emote.random.sub} {emote.list.7tv} {emote:catJAM} "
                + "{emote.channel.top} {emote.user.top} {emote.message.count} {emote.provider:Kappa}"
        );

        rendered.Should().NotBeEmpty();
        await fixture
            .Assets.Received(1)
            .GetChannelEmotesAsync(EmoteHelperFixture.ChannelA, Arg.Any<CancellationToken>());
        await fixture.Assets.Received(1).GetGlobalEmotesAsync(Arg.Any<CancellationToken>());
        await fixture
            .Catalogue.Received(1)
            .GetForChannelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<NomNomzBot.Domain.Chat.Enums.ChatEmoteSender>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_render_without_emote_tokens_calls_no_emote_service()
    {
        await using EmoteHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("hello {text.upper:world}");

        rendered.Should().Be("hello WORLD");
        await fixture
            .Assets.DidNotReceive()
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bad_number_or_a_missing_argument_prints_the_token_as_written()
    {
        await using EmoteHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{emote.repeat:catJAM|0} {emote.repeat:catJAM|51} {emote.repeat:catJAM|x} "
                + "{emote.wall:catJAM|11|2} {emote.wall:catJAM|2|21} {emote} {emote.cheermote:0}"
        );

        rendered
            .Should()
            .Be(
                "{emote.repeat:catJAM|0} {emote.repeat:catJAM|51} {emote.repeat:catJAM|x} "
                    + "{emote.wall:catJAM|11|2} {emote.wall:catJAM|2|21} {emote} {emote.cheermote:0}"
            );
    }

    [Fact]
    public async Task When_twitch_cannot_be_reached_the_twitch_rows_read_as_empty_and_count_zero()
    {
        await using EmoteHelperFixture fixture = new(assetsFail: true);

        string rendered = await fixture.RenderAsync(
            "[{emote.count.channel}] [{emote.list.channel}] [{emote.random}] [{emote.random.global}] [{emote:catJAM}]"
        );

        rendered.Should().Be("[0] [] [] [] [catJAM]");
    }

    [Fact]
    public async Task A_message_without_emotes_counts_zero_and_has_no_first_emote()
    {
        await using EmoteHelperFixture fixture = new();
        Dictionary<string, string> seeds = EmoteHelperFixture.Seeds();
        seeds["message"] = "hello there KEKWait";

        string rendered = await fixture.RenderAsync(
            "[{emote.message.count}] [{emote.message.first}]",
            seeds
        );

        rendered.Should().Be("[0] []");
    }

    [Fact]
    public async Task The_top_emote_this_stream_ignores_the_lines_of_earlier_streams()
    {
        await using EmoteHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "[{emote.channel.top.stream}] [{emote.channel.top}]"
        );

        rendered.Should().Be("[catJAM] [KEKW]");
    }

    private static List<TemplateHelperEntry> EmoteEntries(EmoteHelperFixture fixture) =>
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "emote" }
            ),
        ];
}
