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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A channel that has not opted to keep rewards active while offline has its rewards switched off at Twitch when
/// the stream ends and back on when it starts. Only rewards the bot itself switched off come back; a reward the
/// streamer turned off by hand stays off.
/// </summary>
public sealed class OfflineRewardSwitchTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000a2"
    );
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000a9");

    private static readonly UpdateCustomRewardRequest TurnOff = new(IsEnabled: false);
    private static readonly UpdateCustomRewardRequest TurnOn = new(IsEnabled: true);

    private sealed record Harness(
        AuthDbContext Db,
        ITwitchChannelPointsApi Twitch,
        IServiceScopeFactory Scopes
    );

    private static Harness Build(bool keepActive, bool isLive)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel(Broadcaster, "streamer1", keepActive, isLive));
        db.Channels.Add(NewChannel(OtherBroadcaster, "streamer2", keepActive: false, isLive));
        db.SaveChanges();

        ITwitchChannelPointsApi twitch = Substitute.For<ITwitchChannelPointsApi>();
        twitch
            .UpdateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<UpdateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(HelixReward(call.ArgAt<string>(1))));

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(twitch)
            .AddSingleton<IOfflineRewardService>(
                new OfflineRewardService(db, twitch, NullLogger<OfflineRewardService>.Instance)
            )
            .BuildServiceProvider();
        return new(db, twitch, provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static NomNomzBot.Domain.Identity.Entities.Channel NewChannel(
        Guid id,
        string name,
        bool keepActive,
        bool isLive
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = "tw-" + name,
            TwitchChannelId = "tw-" + name,
            Name = name,
            NameNormalized = name,
            IsOnboarded = true,
            IsLive = isLive,
            KeepActiveWhileOffline = keepActive,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static Reward NewReward(
        string twitchId,
        bool isEnabled = true,
        bool isManageable = true,
        bool disabledForOffline = false,
        Guid? channel = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel ?? Broadcaster,
            Title = "Reward " + twitchId,
            Cost = 100,
            IsEnabled = isEnabled,
            IsManageable = isManageable,
            DisabledForOffline = disabledForOffline,
            TwitchRewardId = twitchId,
        };

    private static async Task OffAsync(Harness h) =>
        await new OfflineRewardsOfflineHandler(h.Scopes).HandleAsync(
            new ChannelOfflineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer1",
                StreamDuration = TimeSpan.FromHours(1),
            }
        );

    private static async Task OnAsync(Harness h) =>
        await new OfflineRewardsOnlineHandler(h.Scopes).HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer1",
                StreamTitle = "Playing",
                GameName = "Game",
                StartedAt = DateTimeOffset.UnixEpoch,
            }
        );

    private static async Task<Reward> RowAsync(Harness h, string twitchId) =>
        await h.Db.Rewards.AsNoTracking().SingleAsync(r => r.TwitchRewardId == twitchId);

    [Fact]
    public async Task Going_offline_disables_each_manageable_enabled_reward_at_twitch_and_marks_the_row()
    {
        Harness h = Build(keepActive: false, isLive: false);
        h.Db.Rewards.AddRange(NewReward("tw-a"), NewReward("tw-b"));
        await h.Db.SaveChangesAsync();

        await OffAsync(h);

        await h
            .Twitch.Received(1)
            .UpdateCustomRewardAsync(Broadcaster, "tw-a", TurnOff, Arg.Any<CancellationToken>());
        await h
            .Twitch.Received(1)
            .UpdateCustomRewardAsync(Broadcaster, "tw-b", TurnOff, Arg.Any<CancellationToken>());
        foreach (string id in new[] { "tw-a", "tw-b" })
        {
            Reward row = await RowAsync(h, id);
            row.IsEnabled.Should().BeFalse();
            row.DisabledForOffline.Should()
                .BeTrue("the bot turned it off, so it may turn it back on");
        }
    }

    [Fact]
    public async Task Going_offline_leaves_a_hand_disabled_reward_and_a_reward_the_bot_cannot_manage_alone()
    {
        Harness h = Build(keepActive: false, isLive: false);
        h.Db.Rewards.AddRange(
            NewReward("tw-hand", isEnabled: false),
            NewReward("tw-foreign", isManageable: false),
            NewReward("tw-other-channel", channel: OtherBroadcaster)
        );
        await h.Db.SaveChangesAsync();

        await OffAsync(h);

        await h
            .Twitch.DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
        (await RowAsync(h, "tw-hand")).DisabledForOffline.Should().BeFalse();
        (await RowAsync(h, "tw-foreign")).IsEnabled.Should().BeTrue();
        (await RowAsync(h, "tw-foreign")).DisabledForOffline.Should().BeFalse();
        (await RowAsync(h, "tw-other-channel")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_reward_twitch_refuses_is_skipped_unmarked_and_the_rest_still_switch_off()
    {
        Harness h = Build(keepActive: false, isLive: false);
        h.Db.Rewards.AddRange(NewReward("tw-refused"), NewReward("tw-fine"));
        await h.Db.SaveChangesAsync();
        h.Twitch.UpdateCustomRewardAsync(
                Broadcaster,
                "tw-refused",
                TurnOff,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchCustomReward>("Forbidden", "TWITCH_FORBIDDEN"));

        await OffAsync(h);

        Reward refused = await RowAsync(h, "tw-refused");
        refused.IsEnabled.Should().BeTrue("Twitch kept it on, so the row must say so");
        refused.DisabledForOffline.Should().BeFalse();
        Reward fine = await RowAsync(h, "tw-fine");
        fine.IsEnabled.Should().BeFalse();
        fine.DisabledForOffline.Should().BeTrue();
    }

    [Fact]
    public async Task A_channel_that_keeps_rewards_active_while_offline_loses_none_when_the_stream_ends()
    {
        Harness h = Build(keepActive: true, isLive: false);
        h.Db.Rewards.Add(NewReward("tw-a"));
        await h.Db.SaveChangesAsync();

        await OffAsync(h);

        await h
            .Twitch.DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
        Reward row = await RowAsync(h, "tw-a");
        row.IsEnabled.Should().BeTrue();
        row.DisabledForOffline.Should().BeFalse();
    }

    [Fact]
    public async Task Going_online_enables_only_the_marked_rewards_and_clears_the_marker()
    {
        Harness h = Build(keepActive: false, isLive: true);
        h.Db.Rewards.AddRange(
            NewReward("tw-marked", isEnabled: false, disabledForOffline: true),
            NewReward("tw-hand", isEnabled: false)
        );
        await h.Db.SaveChangesAsync();

        await OnAsync(h);

        await h
            .Twitch.Received(1)
            .UpdateCustomRewardAsync(
                Broadcaster,
                "tw-marked",
                TurnOn,
                Arg.Any<CancellationToken>()
            );
        await h
            .Twitch.DidNotReceive()
            .UpdateCustomRewardAsync(
                Arg.Any<Guid>(),
                "tw-hand",
                Arg.Any<UpdateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            );
        Reward marked = await RowAsync(h, "tw-marked");
        marked.IsEnabled.Should().BeTrue();
        marked.DisabledForOffline.Should().BeFalse();
        Reward hand = await RowAsync(h, "tw-hand");
        hand.IsEnabled.Should().BeFalse("the streamer turned it off, not the bot");
    }

    [Fact]
    public async Task A_marked_reward_twitch_refuses_to_enable_keeps_its_marker_for_the_next_try()
    {
        Harness h = Build(keepActive: false, isLive: true);
        h.Db.Rewards.Add(NewReward("tw-marked", isEnabled: false, disabledForOffline: true));
        await h.Db.SaveChangesAsync();
        h.Twitch.UpdateCustomRewardAsync(
                Broadcaster,
                "tw-marked",
                TurnOn,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchCustomReward>("Forbidden", "TWITCH_FORBIDDEN"));

        await OnAsync(h);

        Reward row = await RowAsync(h, "tw-marked");
        row.DisabledForOffline.Should().BeTrue();
        row.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_the_setting_on_while_offline_enables_the_marked_rewards_at_once()
    {
        Harness h = Build(keepActive: true, isLive: false);
        h.Db.Rewards.Add(NewReward("tw-marked", isEnabled: false, disabledForOffline: true));
        await h.Db.SaveChangesAsync();

        await new OfflineRewardsSettingHandler(h.Scopes).HandleAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = Broadcaster,
                Domain = "channel-settings",
                Action = "updated",
            }
        );

        await h
            .Twitch.Received(1)
            .UpdateCustomRewardAsync(
                Broadcaster,
                "tw-marked",
                TurnOn,
                Arg.Any<CancellationToken>()
            );
        Reward row = await RowAsync(h, "tw-marked");
        row.IsEnabled.Should().BeTrue();
        row.DisabledForOffline.Should().BeFalse();
    }

    [Fact]
    public async Task A_settings_change_that_leaves_the_setting_off_or_the_channel_live_enables_nothing()
    {
        Harness stillOff = Build(keepActive: false, isLive: false);
        stillOff.Db.Rewards.Add(NewReward("tw-marked", isEnabled: false, disabledForOffline: true));
        await stillOff.Db.SaveChangesAsync();
        Harness live = Build(keepActive: true, isLive: true);
        live.Db.Rewards.Add(NewReward("tw-marked", isEnabled: false, disabledForOffline: true));
        await live.Db.SaveChangesAsync();
        ChannelConfigChangedEvent change = new()
        {
            BroadcasterId = Broadcaster,
            Domain = "channel-settings",
            Action = "updated",
        };

        await new OfflineRewardsSettingHandler(stillOff.Scopes).HandleAsync(change);
        await new OfflineRewardsSettingHandler(live.Scopes).HandleAsync(change);

        await stillOff
            .Twitch.DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
        await live
            .Twitch.DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
        (await RowAsync(stillOff, "tw-marked")).DisabledForOffline.Should().BeTrue();
    }

    private static TwitchCustomReward HelixReward(string id) =>
        new(
            BroadcasterId: "tw-streamer1",
            BroadcasterLogin: "streamer1",
            BroadcasterName: "Streamer1",
            Id: id,
            Title: "Reward " + id,
            Prompt: "",
            Cost: 100,
            Image: null,
            DefaultImage: new("1x", "2x", "4x"),
            BackgroundColor: "#000000",
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new(false, 0),
            MaxPerUserPerStreamSetting: new(false, 0),
            GlobalCooldownSetting: new(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: 0,
            CooldownExpiresAt: null
        );
}
