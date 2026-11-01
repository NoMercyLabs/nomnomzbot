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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves what the voice says for a channel's own emotes: the emote's name without the channel prefix, read from
/// the channel's real emote list, for every spoken line (the lexicon every speech path shares). The streamer's own
/// pronunciation rule for an emote wins, and a failed emote read leaves the emotes spoken as written.
/// </summary>
public sealed class TtsEmoteRulesServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-4444-7000-8000-000000000001");

    private static readonly TwitchEmoteImages NoImages = new("", "", "");

    private static TwitchChannelEmote Emote(string name) =>
        new($"id-{name}", name, NoImages, "1000", "subscriptions", "set-1", [], [], []);

    private sealed class Harness
    {
        public required TtsEmoteRulesService Rules { get; init; }
        public required TtsLexiconService Lexicon { get; init; }
        public required ITwitchChatAssetsApi Assets { get; init; }
        public required TtsTestDbContext Db { get; init; }
    }

    private static Harness Build(params string[] channelEmotes)
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                        .. channelEmotes.Select(Emote),
                    ])
                )
            );

        MemoryCacheService cache = new(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance
        );
        TtsEmoteRulesService rules = new(assets, new SpokenNameFormatter(), cache);

        TtsTestDbContext db = TtsTestDbContext.New();
        IChannelNamePronunciationService names = Substitute.For<IChannelNamePronunciationService>();
        names
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ChannelNamePronunciation>>([]));

        return new()
        {
            Rules = rules,
            Lexicon = new(db, cache, names, rules),
            Assets = assets,
            Db = db,
        };
    }

    [Fact]
    public async Task A_chat_line_with_an_emote_is_spoken_with_the_name_of_the_emote_only()
    {
        Harness h = Build("stoney90Hype", "stoney90Hug", "stoney90HypeTrain");

        string spoken = await h.Lexicon.ApplyAsync(
            Tenant,
            "that was stoney90HypeTrain , thanks stoney90Hug"
        );

        spoken.Should().Be("that was Hype Train , thanks Hug");
    }

    [Fact]
    public async Task A_word_that_only_contains_the_prefix_is_left_alone()
    {
        Harness h = Build("stoney90Hype", "stoney90Hug");

        (await h.Lexicon.ApplyAsync(Tenant, "stoney90 is my channel and mystoney90Hype too"))
            .Should()
            .Be("stoney90 is my channel and mystoney90Hype too");
    }

    [Fact]
    public async Task The_streamers_own_rule_for_an_emote_wins_over_the_emote_name()
    {
        Harness h = Build("stoney90Hype", "stoney90Hug");
        await h.Lexicon.CreateAsync(
            Tenant,
            new() { Phrase = "stoney90Hype", Replacement = "hype yeah" }
        );

        (await h.Lexicon.ApplyAsync(Tenant, "stoney90Hype")).Should().Be("hype yeah");
    }

    [Fact]
    public async Task A_channel_whose_emotes_do_not_agree_on_a_prefix_has_its_emotes_spoken_as_written()
    {
        Harness h = Build("gachiGASM", "gachiBASS", "Kappa");

        (await h.Lexicon.ApplyAsync(Tenant, "gachiGASM Kappa")).Should().Be("gachiGASM Kappa");
    }

    [Fact]
    public async Task A_failed_emote_read_leaves_the_text_unchanged_and_is_asked_again_next_time()
    {
        Harness h = Build();
        h.Assets.ClearReceivedCalls();
        h.Assets.GetChannelEmotesAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Failure<IReadOnlyList<TwitchChannelEmote>>("down", "UPSTREAM")
                )
            );

        (await h.Lexicon.ApplyAsync(Tenant, "stoney90Hype")).Should().Be("stoney90Hype");
        (await h.Lexicon.ApplyAsync(Tenant, "stoney90Hype")).Should().Be("stoney90Hype");

        await h.Assets.Received(2).GetChannelEmotesAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_emote_list_is_read_once_and_served_from_the_cache_after_that()
    {
        Harness h = Build("stoney90Hype", "stoney90Hug");

        await h.Rules.RulesAsync(Tenant);
        await h.Rules.RulesAsync(Tenant);

        await h.Assets.Received(1).GetChannelEmotesAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_rules_carry_the_spoken_name_as_a_word_rule()
    {
        Harness h = Build("stoney90Hype", "stoney90Hug");

        IReadOnlyList<TtsLexiconEntryDto> rules = await h.Rules.RulesAsync(Tenant);

        rules.Should().Contain(r => r.Phrase == "stoney90Hug" && r.Replacement == "Hug");
    }
}
