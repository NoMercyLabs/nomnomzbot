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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Rewards;
using NomNomzBot.Infrastructure.Tests.Seeding;
using NSubstitute;
using GrantedStanding = NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A seeder or sync that needs a scope the channel's token lacks reads the stored scopes first, skips quietly (no
/// Twitch call, no warning), and records one action-required item per channel and scope. A repeat run writes no
/// second item. Each site runs its real class over a real test database; only the Twitch client is faked.
/// </summary>
public sealed class ScopeGatedSeedersTests
{
    private const string BotUserId = "1335549269";
    private const string BotLogin = "nomz_bot";

    private readonly AuthDbContext _db = AuthTestBuilder.NewContext();
    private readonly ScopeGateTestKit _kit;
    private readonly Guid _broadcaster = Guid.NewGuid();

    public ScopeGatedSeedersTests()
    {
        _kit = new(_db);
    }

    // ── The gate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gate_refuses_a_missing_scope_and_records_exactly_one_inbox_item_however_often_it_runs()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");

        bool first = await _kit.Gate.HasScopeOrRecordAsync(
            _broadcaster,
            TwitchScopes.ModerationRead
        );
        bool second = await _kit.Gate.HasScopeOrRecordAsync(
            _broadcaster,
            TwitchScopes.ModerationRead
        );

        first.Should().BeFalse();
        second.Should().BeFalse();
        (await _kit.InboxScopesAsync(_broadcaster)).Should().Equal(TwitchScopes.ModerationRead);
    }

    [Fact]
    public async Task Gate_lets_a_held_scope_through_and_records_nothing()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ModerationRead);

        bool has = await _kit.Gate.HasScopeOrRecordAsync(_broadcaster, TwitchScopes.ModerationRead);

        has.Should().BeTrue();
        (await _kit.InboxScopesAsync(_broadcaster)).Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_skips_a_channel_without_a_twitch_connection_and_records_nothing()
    {
        await SeedChannelAsync();

        bool has = await _kit.Gate.HasScopeOrRecordAsync(_broadcaster, TwitchScopes.ModerationRead);

        has.Should().BeFalse();
        (await _kit.InboxScopesAsync(_broadcaster)).Should().BeEmpty();
    }

    // ── Site 1: bot join moderator grant ────────────────────────────────────

    [Fact]
    public async Task BotJoin_without_the_manage_moderators_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await SeedSharedBotAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<BotJoinOnOnboardingHandler> log = new();
        BotJoinOnOnboardingHandler sut = new(
            _db,
            moderators,
            _kit.Gate,
            PinnedBot(),
            TestSanction.Held(),
            log
        );

        await sut.HandleAsync(Onboarded());
        await sut.HandleAsync(Onboarded());

        await moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .Equal(TwitchScopes.ChannelManageModerators);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task BotJoin_with_the_manage_moderators_scope_still_grants_moderator()
    {
        await SeedChannelAsync();
        await SeedSharedBotAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ChannelManageModerators);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(_broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        BotJoinOnOnboardingHandler sut = new(
            _db,
            moderators,
            _kit.Gate,
            PinnedBot(),
            TestSanction.Held(),
            new ListLogger<BotJoinOnOnboardingHandler>()
        );

        await sut.HandleAsync(Onboarded());

        await moderators
            .Received(1)
            .AddModeratorAsync(_broadcaster, BotUserId, Arg.Any<CancellationToken>());
        (await _kit.InboxScopesAsync(_broadcaster)).Should().BeEmpty();
    }

    // ── Site 2: bot moderator self-heal grant ───────────────────────────────

    [Fact]
    public async Task SelfHeal_without_the_manage_moderators_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await SeedSharedBotAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<BotModeratorGrantService> log = new();
        BotModeratorGrantService sut = SelfHeal(moderators, log);

        Result<bool> first = await sut.TryGrantAsync(_broadcaster, new(BotUserId, BotLogin));
        Result<bool> second = await sut.TryGrantAsync(_broadcaster, new(BotUserId, BotLogin));

        first.Value.Should().BeFalse();
        second.Value.Should().BeFalse();
        await moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .Equal(TwitchScopes.ChannelManageModerators);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task SelfHeal_with_the_manage_moderators_scope_still_grants_moderator()
    {
        await SeedChannelAsync();
        await SeedSharedBotAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ChannelManageModerators);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(_broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result<bool> granted = await SelfHeal(
                moderators,
                new ListLogger<BotModeratorGrantService>()
            )
            .TryGrantAsync(_broadcaster, new(BotUserId, BotLogin));

        granted.Value.Should().BeTrue();
        await moderators
            .Received(1)
            .AddModeratorAsync(_broadcaster, BotUserId, Arg.Any<CancellationToken>());
    }

    // ── Site 3: rewards ─────────────────────────────────────────────────────

    [Fact]
    public async Task RewardSync_without_the_redemptions_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        ListLogger<RewardService> log = new();
        RewardService sut = Rewards(points, log);

        Result first = await sut.SyncWithTwitchAsync(_broadcaster.ToString());
        Result second = await sut.SyncWithTwitchAsync(_broadcaster.ToString());

        first.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        second.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        await points
            .DidNotReceiveWithAnyArgs()
            .GetCustomRewardsAsync(default, default, default, default);
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .Equal(TwitchScopes.ChannelReadRedemptions);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RewardSync_with_the_redemptions_scope_still_pulls_the_rewards()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ChannelReadRedemptions);
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        points
            .GetCustomRewardsAsync(
                _broadcaster,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([TwitchReward()]));

        Result result = await Rewards(points, new ListLogger<RewardService>())
            .SyncWithTwitchAsync(_broadcaster.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        List<Reward> stored = await _db
            .Rewards.Where(r => r.BroadcasterId == _broadcaster)
            .ToListAsync();
        stored.Should().ContainSingle(r => r.Title == "Hydrate" && r.Cost == 100);
    }

    [Fact]
    public async Task RewardSeed_handler_logs_a_missing_scope_refusal_below_warning()
    {
        Application.Rewards.Services.IRewardService rewards =
            Substitute.For<Application.Rewards.Services.IRewardService>();
        rewards
            .SyncWithTwitchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ITwitchScopeGateService.MissingScope(TwitchScopes.ChannelReadRedemptions));
        ListLogger<RewardSeedOnOnboardingHandler> log = new();

        await new RewardSeedOnOnboardingHandler(rewards, log).HandleAsync(Onboarded());

        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ModeratorRosterSeed_handler_logs_a_missing_scope_refusal_below_warning()
    {
        ICommunityRosterService roster = Substitute.For<ICommunityRosterService>();
        roster
            .SyncModeratorsFromTwitchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ITwitchScopeGateService.MissingScope<int>(TwitchScopes.ModerationRead));
        ListLogger<ModeratorRosterSeedOnOnboardingHandler> log = new();

        await new ModeratorRosterSeedOnOnboardingHandler(roster, log).HandleAsync(Onboarded());

        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    // ── Site 4: subscriber standing ─────────────────────────────────────────

    [Fact]
    public async Task SubscriberSeed_without_the_subscriptions_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchSubscriptionsApi subscriptions = Substitute.For<ITwitchSubscriptionsApi>();
        ListLogger<SubscriberStandingSeedOnOnboardingHandler> log = new();
        SubscriberStandingSeedOnOnboardingHandler sut = new(
            Standings(),
            KnownUser(),
            subscriptions,
            _kit.Gate,
            log
        );

        await sut.HandleAsync(Onboarded());
        await sut.HandleAsync(Onboarded());

        await subscriptions
            .DidNotReceiveWithAnyArgs()
            .GetBroadcasterSubscriptionsAsync(default, default, default!, default);
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .Equal(TwitchScopes.ChannelReadSubscriptions);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task SubscriberSeed_with_the_subscriptions_scope_still_seeds_the_standing()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ChannelReadSubscriptions);
        Guid subscriber = Guid.NewGuid();
        ITwitchSubscriptionsApi subscriptions = Substitute.For<ITwitchSubscriptionsApi>();
        subscriptions
            .GetBroadcasterSubscriptionsAsync(
                _broadcaster,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBroadcasterSubscription>(
                        [
                            new(
                                "b",
                                "b",
                                "B",
                                "",
                                "",
                                "",
                                false,
                                "Plan",
                                "2000",
                                "tw-sub-1",
                                "subone",
                                "SubOne"
                            ),
                        ],
                        null,
                        1
                    )
                )
            );
        SubscriberStandingSeedOnOnboardingHandler sut = new(
            Standings(),
            KnownUser(subscriber),
            subscriptions,
            _kit.Gate,
            new ListLogger<SubscriberStandingSeedOnOnboardingHandler>()
        );

        await sut.HandleAsync(Onboarded());

        GrantedStanding stored = await _db.ChannelCommunityStandings.SingleAsync(s =>
            s.BroadcasterId == _broadcaster
        );
        stored.UserId.Should().Be(subscriber);
        stored.Standing.Should().Be(CommunityStanding.Subscriber);
        stored.SubTier.Should().Be("2000");
    }

    // ── Site 5: VIP standing ────────────────────────────────────────────────

    [Fact]
    public async Task VipSeed_without_the_vips_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<VipStandingSeedOnOnboardingHandler> log = new();
        VipStandingSeedOnOnboardingHandler sut = new(
            Standings(),
            KnownUser(),
            moderators,
            _kit.Gate,
            log
        );

        await sut.HandleAsync(Onboarded());
        await sut.HandleAsync(Onboarded());

        await moderators.DidNotReceiveWithAnyArgs().GetVipsAsync(default, default!, default);
        (await _kit.InboxScopesAsync(_broadcaster)).Should().Equal(TwitchScopes.ChannelReadVips);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task VipSeed_with_the_vips_scope_still_seeds_the_standing()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ChannelReadVips);
        Guid vip = Guid.NewGuid();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetVipsAsync(_broadcaster, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchVip>([new("tw-vip-1", "VipOne", "vipone")], null, 1)
                )
            );
        VipStandingSeedOnOnboardingHandler sut = new(
            Standings(),
            KnownUser(vip),
            moderators,
            _kit.Gate,
            new ListLogger<VipStandingSeedOnOnboardingHandler>()
        );

        await sut.HandleAsync(Onboarded());

        GrantedStanding stored = await _db.ChannelCommunityStandings.SingleAsync(s =>
            s.BroadcasterId == _broadcaster
        );
        stored.UserId.Should().Be(vip);
        stored.Standing.Should().Be(CommunityStanding.Vip);
    }

    // ── Site 6: banned users ────────────────────────────────────────────────

    [Fact]
    public async Task BannedImport_without_the_moderation_scope_makes_no_call_and_records_one_item()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        ListLogger<BannedUserImportOnOnboardingHandler> log = new();
        BannedUserImportOnOnboardingHandler sut = new(
            OwnScopeFactory(),
            moderation,
            _kit.Gate,
            TimeProvider.System,
            log
        );

        await sut.HandleAsync(Onboarded());
        await sut.HandleAsync(Onboarded());

        await moderation.DidNotReceiveWithAnyArgs().GetBannedUsersAsync(default, default!, default);
        (await _kit.InboxScopesAsync(_broadcaster)).Should().Equal(TwitchScopes.ModerationRead);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task BannedImport_with_the_moderation_scope_still_imports_the_ban()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ModerationRead);
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(
                _broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            new(
                                "tw-ban-1",
                                "banone",
                                "BanOne",
                                null,
                                DateTimeOffset.UtcNow,
                                "spam",
                                "m1",
                                "modone",
                                "ModOne"
                            ),
                        ],
                        null,
                        1
                    )
                )
            );
        BannedUserImportOnOnboardingHandler sut = new(
            OwnScopeFactory(),
            moderation,
            _kit.Gate,
            TimeProvider.System,
            new ListLogger<BannedUserImportOnOnboardingHandler>()
        );

        await sut.HandleAsync(Onboarded());

        List<Configuration> rows = await _db
            .Configurations.Where(c => c.BroadcasterId == _broadcaster && c.Key.StartsWith("ban:"))
            .ToListAsync();
        rows.Should().ContainSingle(r => r.Key == "ban:tw-ban-1");
    }

    // ── Site 7: moderator and VIP roster sync ───────────────────────────────

    [Fact]
    public async Task RosterSync_without_either_scope_makes_no_call_and_records_one_item_per_scope()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<CommunityRosterService> log = new();
        CommunityRosterService sut = new(_db, moderators, _kit.Gate, TimeProvider.System, log);

        Result<int> first = await sut.SyncModeratorsFromTwitchAsync(_broadcaster);
        Result<int> second = await sut.SyncModeratorsFromTwitchAsync(_broadcaster);

        first.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        second.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        await moderators.DidNotReceiveWithAnyArgs().GetModeratorsAsync(default, default!, default);
        await moderators.DidNotReceiveWithAnyArgs().GetVipsAsync(default, default!, default);
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .BeEquivalentTo([TwitchScopes.ModerationRead, TwitchScopes.ChannelReadVips]);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RosterSync_with_only_the_moderation_scope_still_syncs_moderators_and_skips_vips()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, TwitchScopes.ModerationRead);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratorsAsync(
                _broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>([new("tw-mod-1", "modone", "ModOne")], null, 1)
                )
            );
        ListLogger<CommunityRosterService> log = new();
        CommunityRosterService sut = new(_db, moderators, _kit.Gate, TimeProvider.System, log);

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(_broadcaster);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(1);
        await moderators.DidNotReceiveWithAnyArgs().GetVipsAsync(default, default!, default);
        (await _db.ChannelModerators.CountAsync(m => m.ChannelId == _broadcaster)).Should().Be(1);
        (await _kit.InboxScopesAsync(_broadcaster)).Should().Equal(TwitchScopes.ChannelReadVips);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    // ── Site 8: gap backfill ────────────────────────────────────────────────

    [Fact]
    public async Task GapBackfill_without_the_scopes_makes_no_call_and_records_one_item_per_scope()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(_broadcaster, "chat:read");
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        RecordingEventBus bus = new();
        ListLogger<EventSubGapBackfillService> log = new();
        EventSubGapBackfillService sut = Backfill(points, channels, bus, log);
        DateTimeOffset start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

        await sut.BackfillGapAsync(_broadcaster, start, start.AddMinutes(30));
        await sut.BackfillGapAsync(_broadcaster, start, start.AddMinutes(30));

        await points
            .DidNotReceiveWithAnyArgs()
            .GetCustomRewardsAsync(default, default, default, default);
        await channels
            .DidNotReceiveWithAnyArgs()
            .GetChannelFollowersAsync(default, default!, default);
        (await _kit.InboxScopesAsync(_broadcaster))
            .Should()
            .BeEquivalentTo([
                TwitchScopes.ChannelReadRedemptions,
                TwitchScopes.ModeratorReadFollowers,
            ]);
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task GapBackfill_with_the_scopes_still_replays_a_missed_follow()
    {
        await SeedChannelAsync();
        await _kit.GrantAsync(
            _broadcaster,
            TwitchScopes.ChannelReadRedemptions,
            TwitchScopes.ModeratorReadFollowers
        );
        DateTimeOffset start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        points
            .GetCustomRewardsAsync(
                _broadcaster,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelFollowersAsync(
                _broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchChannelFollower>(
                        [new("u1", "loginu1", "Nameu1", start.AddMinutes(5))],
                        null,
                        1
                    )
                )
            );
        RecordingEventBus bus = new();

        Result<int> result = await Backfill(
                points,
                channels,
                bus,
                new ListLogger<EventSubGapBackfillService>()
            )
            .BackfillGapAsync(_broadcaster, start, start.AddMinutes(30));

        result.Value.Should().Be(1);
        bus.Published.Should().ContainSingle();
        (await _kit.InboxScopesAsync(_broadcaster)).Should().BeEmpty();
    }

    // ── Builders ────────────────────────────────────────────────────────────

    private ChannelOnboardedEvent Onboarded() =>
        new()
        {
            BroadcasterId = _broadcaster,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = "tw-channel",
            Name = "stoney",
        };

    private async Task SeedChannelAsync()
    {
        _db.Channels.Add(
            new()
            {
                Id = _broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "tw-channel",
                Name = "stoney",
                NameNormalized = "stoney",
                IsOnboarded = true,
            }
        );
        await _db.SaveChangesAsync();
    }

    private async Task SeedSharedBotAsync()
    {
        _db.BotAccounts.Add(
            new()
            {
                Id = Guid.NewGuid(),
                IdentityType = AuthEnums.BotIdentityType.Shared,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = BotUserId,
                BotUsername = BotLogin,
                IsActive = true,
                ConnectionId = Guid.NewGuid(),
            }
        );
        await _db.SaveChangesAsync();
    }

    private static IConfiguration PinnedBot() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Twitch:BotUserId"] = BotUserId }
            )
            .Build();

    private BotModeratorGrantService SelfHeal(
        ITwitchModeratorsApi moderators,
        ILogger<BotModeratorGrantService> log
    ) =>
        new(
            _db,
            moderators,
            _kit.Gate,
            PinnedBot(),
            TestSanction.Held(),
            new(TimeProvider.System),
            log
        );

    private RewardService Rewards(ITwitchChannelPointsApi points, ILogger<RewardService> log) =>
        new(
            _db,
            points,
            TimeProvider.System,
            log,
            RewardServiceTestProvider.Create(),
            scopeGate: _kit.Gate
        );

    private EventSubGapBackfillService Backfill(
        ITwitchChannelPointsApi points,
        ITwitchChannelsApi channels,
        RecordingEventBus bus,
        ILogger<EventSubGapBackfillService> log
    )
    {
        NomNomzBot.Application.Contracts.EventStore.IEventJournal journal =
            Substitute.For<NomNomzBot.Application.Contracts.EventStore.IEventJournal>();
        journal
            .GetExistingEventIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        return new(points, channels, _kit.Gate, journal, bus, log);
    }

    private ICommunityStandingService Standings() =>
        new CommunityStandingService(_db, new RecordingEventBus(), TimeProvider.System);

    private static IUserService KnownUser(Guid? id = null)
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        (id ?? Guid.NewGuid()).ToString(),
                        "user",
                        "User",
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow
                    )
                )
            );
        return users;
    }

    private IServiceScopeFactory OwnScopeFactory() =>
        new ServiceCollection()
            .AddSingleton<Application.Abstractions.Persistence.IApplicationDbContext>(_db)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static TwitchCustomReward TwitchReward() =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "reward-1",
            Title: "Hydrate",
            Prompt: "drink",
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
