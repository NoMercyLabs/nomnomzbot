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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

public sealed class ActivityHelpersBehaviorTests
{
    [Fact]
    public async Task The_scan_registers_the_activity_provider()
    {
        await using BroadcastHelperFixture fixture = new();

        fixture.Registry.ProviderFor("activity.mpm")!.Namespace.Should().Be("activity");
        fixture.Registry.ProviderFor("activity.nope").Should().BeNull();
    }

    [Fact]
    public async Task The_recorder_feeds_a_chat_message_into_the_rolling_windows()
    {
        await using BroadcastHelperFixture fixture = new();
        ChatActivityRecorder recorder = new(
            fixture.Services.GetServices<ITemplateHelperProvider>()
        );

        await recorder.HandleAsync(Message("3001", "NewPerson"));
        string rendered = await fixture.RenderAsync(
            "{activity.mpm} {activity.messages.hour} {activity.chatters.active:10} {activity.last.chatter}"
        );

        rendered.Should().Be("19 1,081 13 NewPerson");
    }

    [Fact]
    public async Task The_trigger_viewer_is_never_the_last_chatter()
    {
        await using BroadcastHelperFixture fixture = new();
        ChatActivityRecorder recorder = new(
            fixture.Services.GetServices<ITemplateHelperProvider>()
        );

        await recorder.HandleAsync(Message("1002", "ViewerTwo"));
        Dictionary<string, string> seeds = BroadcastHelperFixture.Seeds();
        seeds["user.id"] = "1002";

        string rendered = await fixture.RenderAsync("{activity.last.chatter}", seeds);

        rendered.Should().Be("ViewerOne");
    }

    [Fact]
    public async Task Time_moves_the_windows_and_an_old_message_leaves_them()
    {
        await using BroadcastHelperFixture fixture = new();

        fixture.Advance(TimeSpan.FromSeconds(61));
        string soon = await fixture.RenderAsync(
            "{activity.mpm} {activity.mpm:5} {activity.messages.hour}"
        );
        fixture.Advance(TimeSpan.FromMinutes(61));
        string later = await fixture.RenderAsync(
            "{activity.messages.hour} {activity.top.hour} {activity.last.chatter} {activity.random.recent:5}"
        );

        // 61 s later the 16 oldest messages (laid down 3,541 s or more before the fixture clock) left the hour.
        soon.Should().Be("0 12 1,064");
        later
            .Should()
            .Be("0 {activity.top.hour} {activity.last.chatter} {activity.random.recent:5}");
    }

    [Fact]
    public async Task The_random_recent_chatter_is_one_of_the_authors_in_the_window_and_follows_the_seed()
    {
        await using BroadcastHelperFixture fixture = new();
        string[] authorsInOrder = ["ViewerOne", "ViewerTwo"];

        string rendered = await fixture.RenderAsync("{activity.random.recent:5}");

        rendered.Should().Be(authorsInOrder[new Random(1).Next(authorsInOrder.Length)]);
    }

    [Fact]
    public async Task Bad_arguments_leave_the_token_as_written()
    {
        await using BroadcastHelperFixture fixture = new();
        const string template =
            "{activity.mpm:0} {activity.mpm:61} {activity.mpm:x} {activity.chatters.active} "
            + "{activity.top.list:26} {activity.top.list:0} {activity.random.recent:90}";

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(template);
    }

    [Fact]
    public async Task A_dutch_channel_groups_numbers_the_dutch_way_and_names_the_day_in_dutch()
    {
        await using BroadcastHelperFixture fixture = new(language: "nl");

        string rendered = await fixture.RenderAsync(
            "{activity.messages.hour} {activity.messages.today} {activity.busiestDay}"
        );

        rendered.Should().Be("1.080 2.610 vrijdag");
    }

    [Fact]
    public async Task Seven_outside_tokens_in_one_render_read_each_source_once()
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{activity.messages.today} {activity.top.today} {activity.top.today.count} {activity.chatters.now} "
                + "{activity.chatters.lurkers} {activity.busiestHour} {activity.busiestDay}"
        );

        rendered.Should().Be("2,610 ViewerOne 140 51 18 21:00 Friday");
        await fixture
            .Analytics.Received(1)
            .GetDailySeriesAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Analytics.Received(1)
            .GetTopViewersAsync(
                Arg.Any<Guid>(),
                Arg.Any<TopViewerMetric>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Analytics.Received(1)
            .GetChatHoursAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .ChatAssets.Received(1)
            .GetChattersAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Ring_and_channel_rows_call_no_outside_service()
    {
        await using BroadcastHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{activity.mpm} {activity.messages.hour} {activity.chatters.unique} {activity.last.message.ago} {activity.commands.stream}"
        );

        await fixture
            .Analytics.DidNotReceiveWithAnyArgs()
            .GetDailySeriesAsync(default, default, default, default);
        await fixture
            .ChatAssets.DidNotReceiveWithAnyArgs()
            .GetChattersAsync(default, default!, default);
        await fixture.Stats.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
    }

    [Fact]
    public async Task When_a_source_fails_only_its_tokens_print_as_written()
    {
        await using BroadcastHelperFixture fixture = new();
        fixture
            .Analytics.GetDailySeriesAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<IReadOnlyList<ChannelAnalyticsDailyDto>>("down", "error"));

        string rendered = await fixture.RenderAsync(
            "{activity.messages.today} {activity.top.today}"
        );

        rendered.Should().Be("{activity.messages.today} ViewerOne");
    }

    [Fact]
    public void Each_channel_has_its_own_ring_and_old_messages_are_dropped()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(BroadcastHelperFixture.Now);
        ChatActivityRing ring = new(clock);
        Guid other = Guid.Parse("00000000-0000-0000-0000-00000000000b");

        ring.Record(BroadcastHelperFixture.ChannelA, "1", "One");
        clock.Advance(TimeSpan.FromMinutes(59));
        ring.Record(other, "2", "Two");
        clock.Advance(TimeSpan.FromMinutes(2));

        ring.Since(BroadcastHelperFixture.ChannelA, ChatActivityRing.Window).Should().BeEmpty();
        ring.Since(other, ChatActivityRing.Window).Select(e => e.Name).Should().Equal("Two");
    }

    [Fact]
    public async Task Every_activity_entry_has_both_strings_a_sample_and_renders_to_itself()
    {
        await using BroadcastHelperFixture fixture = new();

        BroadcastEntryChecks.AssertAll(fixture.Registry, ["activity"], minimumEntries: 19);
        foreach (
            TemplateHelperEntry entry in BroadcastEntryChecks.EntriesOf(
                fixture.Registry,
                ["activity"]
            )
        )
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    private static ChatMessageReceivedEvent Message(string userId, string name) =>
        new()
        {
            BroadcasterId = BroadcastHelperFixture.ChannelA,
            MessageId = Guid.NewGuid().ToString(),
            TwitchBroadcasterId = "123456",
            UserId = userId,
            UserDisplayName = name,
            UserLogin = name.ToLowerInvariant(),
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
