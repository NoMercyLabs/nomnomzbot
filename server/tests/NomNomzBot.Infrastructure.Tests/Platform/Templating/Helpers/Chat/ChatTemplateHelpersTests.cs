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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

public sealed class ChatTemplateHelpersTests
{
    private static readonly string Long = string.Join(' ', Enumerable.Repeat("word", 100));

    private static Dictionary<string, string> On(string provider)
    {
        Dictionary<string, string> seeds = ChatHelperFixture.Seeds();
        seeds["user.provider"] = provider;
        seeds["long"] = Long;
        return seeds;
    }

    [Theory]
    [InlineData("twitch", 500)]
    [InlineData("kick", 500)]
    [InlineData("youtube", 200)]
    public async Task The_limit_is_the_one_of_the_delivering_platform(string provider, int limit)
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.limit}", On(provider));

        rendered.Should().Be(limit.ToString());
    }

    [Fact]
    public async Task Fit_cuts_to_the_platform_limit_and_ends_with_a_space_and_an_ellipsis()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.fit:{long}}", On("youtube"));

        rendered.Should().Be(Long[..198].TrimEnd() + " …");
        rendered.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Fit_leaves_text_that_already_fits_whole()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.fit:{long}}", On("twitch"));

        rendered.Should().Be(Long);
    }

    [Fact]
    public async Task Fit_with_an_end_text_keeps_the_whole_result_within_the_limit()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{chat.fit:{long}|(more on the page)}",
            On("youtube")
        );

        rendered.Should().Be(Long[..181].TrimEnd() + " (more on the page)");
        rendered.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Fit_words_cuts_at_a_word_edge()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.fit.words:{long}}", On("youtube"));

        rendered.Should().Be(Long[..194] + " …");
        rendered.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Fit_counts_emoji_as_one_character_each()
    {
        await using ChatHelperFixture fixture = new();
        Dictionary<string, string> seeds = On("youtube");
        seeds["long"] = string.Concat(Enumerable.Repeat("\U0001F600", 201));

        string rendered = await fixture.RenderAsync("{chat.fit:{long}}", seeds);

        rendered.Should().Be(string.Concat(Enumerable.Repeat("\U0001F600", 198)) + " …");
    }

    [Fact]
    public async Task A_name_that_differs_from_its_login_is_mentioned_by_the_login()
    {
        await using ChatHelperFixture fixture = new();
        Dictionary<string, string> seeds = ChatHelperFixture.Seeds();
        seeds["user"] = "ビューアー";

        string rendered = await fixture.RenderAsync(
            "{chat.mention.user} {chat.mention:ビューアー} {chat.mention:SomeoneElse}",
            seeds
        );

        rendered.Should().Be("@viewerone @viewerone @SomeoneElse");
    }

    [Fact]
    public async Task Me_is_plain_text_on_a_platform_without_action_lines()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.me:dances}", On("kick"));

        rendered.Should().Be("dances");
    }

    [Fact]
    public async Task In_a_shared_chat_the_other_channels_are_named()
    {
        await using ChatHelperFixture fixture = new(shared: true);

        string rendered = await fixture.RenderAsync(
            "{if:{chat.isShared}|shared chat|} {chat.shared.channels}"
        );

        rendered.Should().Be("shared chat OtherStreamer");
    }

    [Fact]
    public async Task Pinned_is_empty_when_nothing_is_pinned()
    {
        await using ChatHelperFixture fixture = new(pinned: false);

        string rendered = await fixture.RenderAsync("[{chat.pinned}]");

        rendered.Should().Be("[]");
    }

    [Fact]
    public async Task When_the_chat_modes_cannot_be_read_the_token_prints_as_written()
    {
        await using ChatHelperFixture fixture = new(modeReadFails: true);

        string rendered = await fixture.RenderAsync(
            "{chat.settings.slow} {chat.settings.subOnly} {chat.pinned}"
        );

        rendered.Should().Be("{chat.settings.slow} {chat.settings.subOnly} Welcome to the stream");
    }

    [Fact]
    public async Task Many_mode_tokens_in_one_render_read_the_settings_once()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{chat.settings.slow} {chat.settings.followersOnly} {chat.settings.subOnly} {chat.settings.emoteOnly}"
        );

        rendered.Should().Be("3s 10m false false");
        await fixture
            .ChatApi.Received(1)
            .GetChatSettingsAsync(ChatHelperFixture.ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_render_without_chat_service_tokens_calls_no_chat_service()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{chat.mention:ViewerTwo} {chat.limit}");

        rendered.Should().Be("@ViewerTwo 500");
        await fixture
            .ChatApi.DidNotReceive()
            .GetChatSettingsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        fixture.Tracker.DidNotReceive().GetActiveSession(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_render_without_a_channel_renders_pure_rows_and_prints_channel_rows_as_written()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderWithoutChannelAsync(
            "{tts.numbers:3 cats} {chat.mention:ViewerTwo} {chat.pinned} {tts.lexicon:gg}"
        );

        rendered.Should().Be("three cats @ViewerTwo {chat.pinned} {tts.lexicon:gg}");
    }

    [Fact]
    public async Task Tts_plain_drops_the_emotes_of_the_channel_and_keeps_other_capitalised_words()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{tts.plain:KEKW look at catJAM and Kekw www.example.com now}"
        );

        rendered.Should().Be("look at and Kekw now");
    }

    [Fact]
    public async Task Lexicon_gets_the_text_and_returns_the_channel_pronunciation()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{tts.lexicon:gg wp}");

        rendered.Should().Be("good game wp");
        await fixture
            .Lexicon.Received(1)
            .ApplyAsync(ChatHelperFixture.ChannelA, "gg wp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Numbers_are_spoken_in_the_language_of_the_channel()
    {
        await using ChatHelperFixture fixture = new(language: "nl");

        string rendered = await fixture.RenderAsync("{tts.numbers:ik heb 3 katten}");

        rendered.Should().Be("ik heb drie katten");
    }

    [Fact]
    public async Task A_mastodon_instance_must_be_a_plain_host_name()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{link.mastodon:evil.com/x?y=|Live now} {link.mastodon:nodots|Live now} {link.mastodon:a b.social|Live now}"
        );

        rendered
            .Should()
            .Be(
                "{link.mastodon:evil.com/x?y=|Live now} {link.mastodon:nodots|Live now} {link.mastodon:a b.social|Live now}"
            );
    }

    [Fact]
    public async Task Link_text_is_percent_encoded_for_every_reserved_character()
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{link.bluesky:a&b=c #d?}");

        rendered.Should().Be("https://bsky.app/intent/compose?text=a%26b%3Dc%20%23d%3F");
    }

    [Fact]
    public async Task The_scan_registers_the_chat_tts_and_link_providers_and_cuts_the_voice_and_short_link()
    {
        await using ChatHelperFixture fixture = new();

        fixture.Registry.ProviderFor("chat.fit")!.Namespace.Should().Be("chat");
        fixture.Registry.ProviderFor("tts.plain")!.Namespace.Should().Be("tts");
        fixture.Registry.ProviderFor("link.x")!.Namespace.Should().Be("link");
        fixture.Registry.ProviderFor("tts.voice").Should().BeNull();
        fixture.Registry.ProviderFor("link.short").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using ChatHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = ChatEntries(fixture);

        entries.Should().NotBeEmpty();
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
        await using ChatHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in ChatEntries(fixture))
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    private static List<TemplateHelperEntry> ChatEntries(ChatHelperFixture fixture) =>
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "chat" or "tts" or "link" }
            ),
        ];
}
