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
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Platforms;

public sealed class PlatformTemplateHelpersTests
{
    [Fact]
    public async Task The_scan_registers_one_platform_provider_that_owns_the_platform_keys()
    {
        await using PlatformHelperFixture fixture = new();

        ITemplateHelperProvider? provider = fixture.Registry.ProviderFor("platform.viewers.kick");

        provider.Should().NotBeNull();
        provider!.Namespace.Should().Be("platform");
        fixture.Registry.ProviderFor("platform.links").Should().BeSameAs(provider);
        fixture.Registry.ProviderFor("platform.nope").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using PlatformHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = PlatformEntries(fixture);

        entries.Should().HaveCountGreaterThan(20);
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
        await using PlatformHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in PlatformEntries(fixture))
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task A_platform_that_is_not_connected_renders_nothing_and_is_never_read()
    {
        await using PlatformHelperFixture fixture = new(
            new PlatformWorld(Links: [PlatformHelperFixture.AllThree()[0]])
        );

        string rendered = await fixture.RenderAsync(
            "[{platform.viewers.kick}][{platform.link.kick}][{platform.link.youtube}]"
                + "[{platform.followers.twitch}][{platform.isLive:kick}][{platform.title:youtube}]"
                + "[{platform.messages.stream:kick}][{platform.viewers.share:kick}]"
        );

        rendered.Should().Be("[][][][1,234][false][][][]");
        (
            await fixture.RenderAsync(
                "{platform.connected}|{platform.count}|{platform.viewers.total}"
            )
        )
            .Should()
            .Be("Twitch|1|42");
        (await fixture.RenderAsync("{platform.viewers.breakdown}|{platform.live.list}"))
            .Should()
            .Be("Twitch 42|Twitch");
        await fixture.Kick.DidNotReceiveWithAnyArgs().GetChannelAsync(default!, default, default);
        await fixture.YouTube.DidNotReceiveWithAnyArgs().GetActiveLiveChatsAsync(default!, default);
    }

    [Fact]
    public async Task A_channel_that_is_not_live_on_youtube_leaves_it_out_of_the_counts()
    {
        await using PlatformHelperFixture fixture = new(new PlatformWorld(YouTubeIsLive: false));

        string rendered = await fixture.RenderAsync(
            "{platform.viewers.youtube}|{platform.isLive:youtube}|{platform.link.youtube.live}|"
                + "{platform.live.list}|{platform.live.count}|{platform.viewers.total}|{platform.connected}"
        );

        rendered.Should().Be("|false||Twitch, Kick|2|52|Twitch, Kick, YouTube");
        await fixture.YouTube.DidNotReceiveWithAnyArgs().GetActiveLiveChatsAsync(default!, default);
    }

    [Fact]
    public async Task A_kick_read_that_fails_prints_no_viewer_count_and_keeps_the_stored_live_flag()
    {
        await using PlatformHelperFixture fixture = new(new PlatformWorld(KickReadFails: true));

        string rendered = await fixture.RenderAsync(
            "[{platform.viewers.kick}][{platform.isLive:kick}][{platform.viewers.total}]"
                + "[{platform.viewers.breakdown}]"
        );

        rendered.Should().Be("[][true][50][Twitch 42 · YouTube 8]");
    }

    [Fact]
    public async Task A_platform_name_that_is_not_known_prints_the_token_as_written()
    {
        await using PlatformHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{platform.viewers.share:myspace} {platform.isLive:} {platform.title:xyz}"
        );

        rendered
            .Should()
            .Be("{platform.viewers.share:myspace} {platform.isLive:} {platform.title:xyz}");
    }

    [Fact]
    public async Task A_command_typed_on_kick_names_kick_and_a_seed_that_is_missing_prints_the_token()
    {
        await using PlatformHelperFixture fixture = new();
        Dictionary<string, string> seeds = new() { ["user.provider"] = "kick" };

        string rendered = await fixture.RenderAsync(
            "{platform.user} {platform.user.name} {platform.user.icon} {platform.event}",
            seeds
        );

        rendered.Should().Be("kick Kick 🟢 {platform.event}");
    }

    [Fact]
    public async Task The_stream_totals_ask_for_the_days_since_the_stream_started_in_the_channel_zone()
    {
        await using PlatformHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{platform.messages.stream:kick} {platform.chatters.stream:twitch}"
        );

        await fixture
            .Analytics.Received(1)
            .GetPlatformSummaryAsync(
                PlatformHelperFixture.ChannelA,
                new DateOnly(2026, 10, 26),
                new DateOnly(2026, 10, 26),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_connected_platform_without_chat_this_stream_counts_zero_and_an_offline_channel_prints_nothing()
    {
        List<ChannelAnalyticsPlatformSummaryDto> onlyTwitch = [new("twitch", 300, 20, 0, 0, 0)];
        await using PlatformHelperFixture quiet = new(new PlatformWorld(Summaries: onlyTwitch));
        await using PlatformHelperFixture offline = new(new PlatformWorld(StreamIsOn: false));

        (await quiet.RenderAsync("{platform.messages.stream:kick}|{platform.chatters.stream:kick}"))
            .Should()
            .Be("0|0");
        (
            await offline.RenderAsync(
                "{platform.messages.stream:kick}|{platform.chatters.stream:kick}"
            )
        )
            .Should()
            .Be("|");
        await offline
            .Analytics.DidNotReceiveWithAnyArgs()
            .GetPlatformSummaryAsync(default, default, default, default);
    }

    [Fact]
    public async Task Many_platform_tokens_in_one_render_read_each_service_once()
    {
        await using PlatformHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{platform.viewers.total} {platform.viewers.kick} {platform.viewers.youtube} {platform.viewers.breakdown} "
                + "{platform.viewers.share:kick} {platform.live.list} {platform.title:youtube} {platform.top} "
                + "{platform.links} {platform.followers.twitch} {platform.messages.stream:kick} {platform.chatters.stream:youtube}"
        );

        rendered
            .Should()
            .Be(
                "60 10 8 Twitch 42 · Kick 10 · YouTube 8 17% Twitch, Kick, YouTube Building a bot Twitch "
                    + "twitch.tv/samplechannel · kick.com/samplechannel · youtube.com/channel/UCsample 1,234 120 6"
            );
        await fixture
            .Directory.Received(1)
            .ListAsync(
                Arg.Any<IServiceProvider>(),
                PlatformHelperFixture.ChannelA,
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Kick.Received(1)
            .GetChannelAsync("kick-token", 5555, Arg.Any<CancellationToken>());
        await fixture
            .YouTube.Received(1)
            .GetActiveLiveChatsAsync("yt-token", Arg.Any<CancellationToken>());
        await fixture
            .Twitch.Received(1)
            .GetChannelFollowerCountAsync(
                PlatformHelperFixture.ChannelA,
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Analytics.Received(1)
            .GetPlatformSummaryAsync(
                PlatformHelperFixture.ChannelA,
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_render_without_platform_tokens_calls_no_platform_service()
    {
        await using PlatformHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("hello {text.upper:world}");

        rendered.Should().Be("hello WORLD");
        await fixture.Directory.DidNotReceiveWithAnyArgs().ListAsync(default!, default, default);
    }

    [Fact]
    public async Task The_seed_rows_call_no_service_at_all()
    {
        await using PlatformHelperFixture fixture = new();

        await fixture.RenderAsync("{platform.user} {platform.user.icon} {platform.event}");

        await fixture.Directory.DidNotReceiveWithAnyArgs().ListAsync(default!, default, default);
    }

    private static List<TemplateHelperEntry> PlatformEntries(PlatformHelperFixture fixture) =>
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "platform" }
            ),
        ];
}
