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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The chat command, the channel-point reward and the timer start pipelines, so a test run offers each as a
/// trigger sample carrying exactly the variable keys the live trigger sets.
/// </summary>
public sealed class CommandRewardTimerSampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    // The lane's sources have no dependencies, so the scanned catalog can build them without a container.
    private static IReadOnlyList<TriggerSample> Samples()
    {
        List<ITriggerSampleSource> sources =
        [
            .. typeof(TriggerSampleCatalog)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false }
                    && typeof(ITriggerSampleSource).IsAssignableFrom(type)
                    && type.GetConstructor(Type.EmptyTypes) is not null
                )
                .Select(type => (ITriggerSampleSource)Activator.CreateInstance(type)!),
        ];
        return new TriggerSampleCatalog(sources, new FixedClock(Now)).List();
    }

    private static TriggerSample Sample(string id) =>
        Samples().SingleOrDefault(sample => sample.Id == id)
        ?? throw new Xunit.Sdk.XunitException($"The catalog offers no sample '{id}'.");

    [Fact]
    public void The_chat_command_sample_has_the_keys_the_live_handler_seeds_with_no_arguments()
    {
        TriggerSample sample = Sample("command");

        sample.ResponseKey.Should().Be("command");
        ChatMessageReceivedEvent chat = new()
        {
            BroadcasterId = Guid.Parse("0198a000-0000-7000-8000-00000000e001"),
            MessageId = "m",
            TwitchBroadcasterId = "tw",
            UserId = sample.UserId!,
            UserDisplayName = sample.UserDisplayName!,
            UserLogin = "viewer",
            Message = "!hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
        sample
            .Variables.Keys.Should()
            .BeEquivalentTo(ChatMessageHandler.BuildInitialVariables(chat, string.Empty).Keys);
        sample.Variables["user"].Should().Be(sample.UserDisplayName);
        sample.Variables["user.id"].Should().Be(sample.UserId);
    }

    [Fact]
    public void The_reward_sample_runs_the_redemption_event_response_with_the_reward_pipeline_keys()
    {
        TriggerSample sample = Sample("reward.redeemed");

        sample.ResponseKey.Should().Be("channel.channel_points_custom_reward_redemption.add");
        sample
            .Variables.Keys.Should()
            .BeEquivalentTo(
                "user",
                "user.id",
                "reward",
                "reward.id",
                "redemption.id",
                "cost",
                "input"
            );
        sample.Variables["user.id"].Should().Be(sample.UserId);
        int.TryParse(sample.Variables["cost"], out int cost).Should().BeTrue();
        cost.Should().BePositive();
    }

    [Fact]
    public void The_timer_sample_runs_as_the_channel_with_the_timer_name_and_message()
    {
        TriggerSample sample = Sample("timer");

        sample.ResponseKey.Should().Be("timer");
        sample.Variables.Keys.Should().BeEquivalentTo("timer.name", "timer.message");
        sample.UserId.Should().NotBeNullOrWhiteSpace();
        sample.UserDisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_reward_builder_the_live_handler_calls_keeps_the_variables_it_always_seeded()
    {
        RewardRedeemedEvent redemption = new()
        {
            BroadcasterId = Guid.Empty,
            RewardId = "r1",
            RewardTitle = "Title",
            RedemptionId = "d1",
            UserId = "u1",
            UserDisplayName = "Viewer",
            Cost = 250,
            UserInput = null,
        };

        Dictionary<string, string> live = RewardRedeemedHandler.BuildVariables(redemption);

        live.Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["user"] = "Viewer",
                    ["user.id"] = "u1",
                    ["reward"] = "Title",
                    ["reward.id"] = "r1",
                    ["redemption.id"] = "d1",
                    ["cost"] = "250",
                    ["input"] = string.Empty,
                }
            );
        Sample("reward.redeemed").Variables.Keys.Should().BeEquivalentTo(live.Keys);
    }

    [Fact]
    public void The_timer_builder_the_live_service_calls_rotates_messages_and_omits_an_empty_list()
    {
        Timer rotating = new()
        {
            Name = "t",
            Messages = ["a", "b"],
            NextMessageIndex = 3,
        };
        Timer silent = new() { Name = "t", Messages = [] };

        TimerService
            .BuildVariables(rotating)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string> { ["timer.name"] = "t", ["timer.message"] = "b" }
            );
        TimerService
            .BuildVariables(silent)
            .Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["timer.name"] = "t" });
        Sample("timer")
            .Variables.Keys.Should()
            .BeEquivalentTo(TimerService.BuildVariables(rotating).Keys);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
