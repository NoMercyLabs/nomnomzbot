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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Identity.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Seeding;

/// <summary>
/// Proves the onboarding seed-job handlers (the three <c>IEventHandler&lt;ChannelOnboardedEvent&gt;</c>): each
/// calls its own domain's Twitch sync for the event's broadcaster, and each isolates a sync failure — it is
/// caught and logged, the handler never throws, so one failing seed job cannot abort the others.
/// </summary>
public sealed class OnboardingSeedHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000a001");

    private static ChannelOnboardedEvent Event() =>
        new()
        {
            BroadcasterId = Broadcaster,
            OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000a002"),
            TwitchChannelId = "tw-123",
            Name = "stoney_eagle",
        };

    // ── Rewards seed handler ────────────────────────────────────────────────

    [Fact]
    public async Task Reward_handler_syncs_rewards_for_the_event_broadcaster()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SyncWithTwitchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        ListLogger<RewardSeedOnOnboardingHandler> log = new();
        RewardSeedOnOnboardingHandler sut = new(rewards, log);

        await sut.HandleAsync(Event());

        await rewards
            .Received(1)
            .SyncWithTwitchAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reward_handler_catches_and_logs_a_sync_failure_without_throwing()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SyncWithTwitchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));
        ListLogger<RewardSeedOnOnboardingHandler> log = new();
        RewardSeedOnOnboardingHandler sut = new(rewards, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Community moderator-roster seed handler ──────────────────────────────

    [Fact]
    public async Task Community_handler_syncs_moderator_roster_for_the_event_broadcaster()
    {
        ICommunityRosterService roster = Substitute.For<ICommunityRosterService>();
        roster
            .SyncModeratorsFromTwitchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(3));
        ListLogger<ModeratorRosterSeedOnOnboardingHandler> log = new();
        ModeratorRosterSeedOnOnboardingHandler sut = new(roster, log);

        await sut.HandleAsync(Event());

        await roster
            .Received(1)
            .SyncModeratorsFromTwitchAsync(Broadcaster, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Community_handler_catches_and_logs_a_sync_failure_without_throwing()
    {
        ICommunityRosterService roster = Substitute.For<ICommunityRosterService>();
        roster
            .SyncModeratorsFromTwitchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));
        ListLogger<ModeratorRosterSeedOnOnboardingHandler> log = new();
        ModeratorRosterSeedOnOnboardingHandler sut = new(roster, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Membership (Plane-B roles) seed handler ──────────────────────────────

    [Fact]
    public async Task Membership_handler_syncs_the_builders_snapshot_for_the_event_broadcaster()
    {
        IMembershipService membership = Substitute.For<IMembershipService>();
        ITwitchManagementSnapshotBuilder builder =
            Substitute.For<ITwitchManagementSnapshotBuilder>();

        Guid modUserGuid = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
        Guid editorUserGuid = Guid.Parse("0192a000-0000-7000-8000-00000000b002");
        ManagementSnapshot snapshot = new(
            [
                new(modUserGuid, "tw-mod", ManagementRole.Moderator, MembershipSource.TwitchBadge),
                new(
                    editorUserGuid,
                    "tw-editor",
                    ManagementRole.Editor,
                    MembershipSource.HelixEditors
                ),
            ],
            new HashSet<MembershipSource>
            {
                MembershipSource.TwitchBadge,
                MembershipSource.HelixEditors,
            }
        );
        builder.BuildAsync(Broadcaster, Arg.Any<CancellationToken>()).Returns(snapshot);

        membership
            .SyncManagementFromTwitchAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<TwitchManagementMember>>(),
                Arg.Any<IReadOnlySet<MembershipSource>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ListLogger<MembershipSeedOnOnboardingHandler> log = new();
        MembershipSeedOnOnboardingHandler sut = new(membership, builder, log);

        await sut.HandleAsync(Event());

        // The handler forwards the builder's snapshot + its authoritative sources verbatim to the sync.
        await membership
            .Received(1)
            .SyncManagementFromTwitchAsync(
                Broadcaster,
                snapshot.Members,
                snapshot.AuthoritativeSources,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Membership_handler_catches_and_logs_a_failure_without_throwing()
    {
        IMembershipService membership = Substitute.For<IMembershipService>();
        ITwitchManagementSnapshotBuilder builder =
            Substitute.For<ITwitchManagementSnapshotBuilder>();
        builder
            .BuildAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));

        ListLogger<MembershipSeedOnOnboardingHandler> log = new();
        MembershipSeedOnOnboardingHandler sut = new(membership, builder, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        // The downstream sync was never reached, but the failure was contained.
        await membership
            .DidNotReceive()
            .SyncManagementFromTwitchAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<TwitchManagementMember>>(),
                Arg.Any<IReadOnlySet<MembershipSource>>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── Event response seed handler ──────────────────────────────────────────

    [Fact]
    public async Task EventResponse_handler_seeds_the_six_defaults_for_the_event_broadcaster()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ListLogger<EventResponseSeedOnOnboardingHandler> log = new();
        EventResponseSeedOnOnboardingHandler sut = new(new SingleContextScopeFactory(db), log);

        await sut.HandleAsync(Event());

        List<EventResponse> seeded = await db
            .EventResponses.Where(r => r.BroadcasterId == Broadcaster)
            .ToListAsync();

        seeded.Should().HaveCount(6);
        seeded
            .Select(r => r.EventType)
            .Should()
            .BeEquivalentTo([
                "channel.follow",
                "channel.subscribe",
                "channel.subscription.gift",
                "channel.subscription.message",
                "channel.cheer",
                "channel.raid",
            ]);
        seeded.Should().OnlyContain(r => r.IsEnabled && r.ResponseType == "chat_message");
        seeded
            .Single(r => r.EventType == "channel.follow")
            .Message.Should()
            .Be("Welcome {user}! Thanks for the follow!");
    }

    [Fact]
    public async Task EventResponse_handler_is_idempotent_a_second_run_adds_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ListLogger<EventResponseSeedOnOnboardingHandler> log = new();
        EventResponseSeedOnOnboardingHandler sut = new(new SingleContextScopeFactory(db), log);

        await sut.HandleAsync(Event());
        await sut.HandleAsync(Event());

        (await db.EventResponses.CountAsync(r => r.BroadcasterId == Broadcaster)).Should().Be(6);
    }

    [Fact]
    public async Task EventResponse_handler_catches_and_logs_a_failure_without_throwing()
    {
        ListLogger<EventResponseSeedOnOnboardingHandler> log = new();
        EventResponseSeedOnOnboardingHandler sut = new(new ThrowingScopeFactory(), log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Banned-user import seed handler ──────────────────────────────────────

    [Fact]
    public async Task BannedUser_handler_imports_every_page_of_bans_for_the_event_broadcaster()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == null),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            new TwitchBannedUser(
                                "tw-ban-1",
                                "banone",
                                "BanOne",
                                null,
                                DateTimeOffset.UtcNow,
                                "spam",
                                "tw-mod-1",
                                "modone",
                                "ModOne"
                            ),
                        ],
                        "cursor-2",
                        2
                    )
                )
            );
        moderation
            .GetBannedUsersAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == "cursor-2"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            new TwitchBannedUser(
                                "tw-ban-2",
                                "bantwo",
                                "BanTwo",
                                null,
                                DateTimeOffset.UtcNow,
                                "harassment",
                                "tw-mod-1",
                                "modone",
                                "ModOne"
                            ),
                        ],
                        null,
                        2
                    )
                )
            );

        ListLogger<BannedUserImportOnOnboardingHandler> log = new();
        BannedUserImportOnOnboardingHandler sut = new(
            new SingleContextScopeFactory(db),
            moderation,
            TimeProvider.System,
            log
        );

        await sut.HandleAsync(Event());

        List<Configuration> rows = await db
            .Configurations.Where(c => c.BroadcasterId == Broadcaster && c.Key.StartsWith("ban:"))
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Select(r => r.Key).Should().BeEquivalentTo(["ban:tw-ban-1", "ban:tw-ban-2"]);
        rows.Single(r => r.Key == "ban:tw-ban-1").Value.Should().Contain("\"reason\":\"spam\"");
        rows.Single(r => r.Key == "ban:tw-ban-1").Value.Should().Contain("\"bannedBy\":\"ModOne\"");
    }

    [Fact]
    public async Task BannedUser_handler_is_idempotent_and_never_overwrites_an_existing_ban_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            new TwitchBannedUser(
                                "tw-ban-1",
                                "banone",
                                "BanOne",
                                null,
                                DateTimeOffset.UtcNow,
                                "original reason",
                                "tw-mod-1",
                                "modone",
                                "ModOne"
                            ),
                        ],
                        null,
                        1
                    )
                )
            );

        ListLogger<BannedUserImportOnOnboardingHandler> log = new();
        BannedUserImportOnOnboardingHandler sut = new(
            new SingleContextScopeFactory(db),
            moderation,
            TimeProvider.System,
            log
        );

        await sut.HandleAsync(Event());

        // Twitch now reports a different reason for the same ban (e.g. re-worded by a moderator) — the
        // handler must NOT clobber the existing row on re-fire.
        moderation
            .GetBannedUsersAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            new TwitchBannedUser(
                                "tw-ban-1",
                                "banone",
                                "BanOne",
                                null,
                                DateTimeOffset.UtcNow,
                                "changed reason",
                                "tw-mod-1",
                                "modone",
                                "ModOne"
                            ),
                        ],
                        null,
                        1
                    )
                )
            );

        await sut.HandleAsync(Event());

        List<Configuration> rows = await db
            .Configurations.Where(c => c.BroadcasterId == Broadcaster && c.Key.StartsWith("ban:"))
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows.Single().Value.Should().Contain("original reason");
        rows.Single().Value.Should().NotContain("changed reason");
    }

    [Fact]
    public async Task BannedUser_handler_logs_a_warning_and_imports_nothing_when_the_scope_is_missing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchBannedUser>>(
                    "Missing required scope 'moderation:read'.",
                    TwitchErrorCodes.MissingScope
                )
            );

        ListLogger<BannedUserImportOnOnboardingHandler> log = new();
        BannedUserImportOnOnboardingHandler sut = new(
            new SingleContextScopeFactory(db),
            moderation,
            TimeProvider.System,
            log
        );

        await sut.HandleAsync(Event());

        (await db.Configurations.AnyAsync(c => c.BroadcasterId == Broadcaster)).Should().BeFalse();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    // ── Bot mod-join seed handler ─────────────────────────────────────────────

    [Fact]
    public async Task BotJoin_handler_grants_moderator_status_to_the_registered_shared_bot()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.BotAccounts.Add(SharedBot());
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Broadcaster, "tw-bot-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        ListLogger<BotJoinOnOnboardingHandler> log = new();
        BotJoinOnOnboardingHandler sut = new(db, moderators, log);

        await sut.HandleAsync(Event());

        await moderators
            .Received(1)
            .AddModeratorAsync(Broadcaster, "tw-bot-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotJoin_handler_is_a_noop_when_no_shared_bot_is_registered()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();

        ListLogger<BotJoinOnOnboardingHandler> log = new();
        BotJoinOnOnboardingHandler sut = new(db, moderators, log);

        await sut.HandleAsync(Event());

        await moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotJoin_handler_logs_a_warning_when_the_helix_mod_grant_fails()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.BotAccounts.Add(SharedBot());
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Broadcaster, "tw-bot-1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure(
                    "Missing required scope 'channel:manage:moderators'.",
                    TwitchErrorCodes.MissingScope
                )
            );

        ListLogger<BotJoinOnOnboardingHandler> log = new();
        BotJoinOnOnboardingHandler sut = new(db, moderators, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task BotJoin_handler_catches_and_logs_an_unexpected_failure_without_throwing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.BotAccounts.Add(SharedBot());
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Broadcaster, "tw-bot-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));

        ListLogger<BotJoinOnOnboardingHandler> log = new();
        BotJoinOnOnboardingHandler sut = new(db, moderators, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Bot mod-grant backfill (shared bot connects AFTER channels onboarded) ─

    [Fact]
    public async Task BotBackfill_handler_mods_the_bot_on_every_enabled_onboarded_channel()
    {
        Guid onboardedA = Guid.Parse("0192a000-0000-7000-8000-00000000c0a1");
        Guid onboardedB = Guid.Parse("0192a000-0000-7000-8000-00000000c0b2");
        Guid notOnboarded = Guid.Parse("0192a000-0000-7000-8000-00000000c0c3");
        Guid disabled = Guid.Parse("0192a000-0000-7000-8000-00000000c0d4");

        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount bot = SharedBot();
        db.BotAccounts.Add(bot);
        db.Channels.Add(Channel(onboardedA, "tw-a", "chan_a", isOnboarded: true));
        db.Channels.Add(Channel(onboardedB, "tw-b", "chan_b", isOnboarded: true));
        db.Channels.Add(Channel(notOnboarded, "tw-c", "chan_c", isOnboarded: false));
        Channel disabledChannel = Channel(disabled, "tw-d", "chan_d", isOnboarded: true);
        disabledChannel.Enabled = false;
        db.Channels.Add(disabledChannel);
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Arg.Any<Guid>(), "tw-bot-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        ListLogger<BotModGrantOnBotAuthorizedHandler> log = new();
        BotModGrantOnBotAuthorizedHandler sut = new(db, moderators, log);

        await sut.HandleAsync(BotAuthorized(bot.Id));

        // Every LIVE channel got the mod-grant; the not-onboarded and disabled ones were skipped.
        await moderators
            .Received(1)
            .AddModeratorAsync(onboardedA, "tw-bot-1", Arg.Any<CancellationToken>());
        await moderators
            .Received(1)
            .AddModeratorAsync(onboardedB, "tw-bot-1", Arg.Any<CancellationToken>());
        await moderators
            .DidNotReceive()
            .AddModeratorAsync(notOnboarded, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await moderators
            .DidNotReceive()
            .AddModeratorAsync(disabled, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotBackfill_handler_is_a_noop_for_a_non_shared_bot_identity()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount bot = SharedBot();
        db.BotAccounts.Add(bot);
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<BotModGrantOnBotAuthorizedHandler> log = new();
        BotModGrantOnBotAuthorizedHandler sut = new(db, moderators, log);

        await sut.HandleAsync(
            new BotAccountAuthorizedEvent
            {
                BroadcasterId = Guid.Empty,
                BotAccountId = bot.Id,
                IdentityType = AuthEnums.BotIdentityType.Custom,
                BotUsername = "custombot",
            }
        );

        await moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotBackfill_handler_continues_past_a_failing_channel()
    {
        Guid failing = Guid.Parse("0192a000-0000-7000-8000-00000000c1a1");
        Guid succeeding = Guid.Parse("0192a000-0000-7000-8000-00000000c1b2");

        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount bot = SharedBot();
        db.BotAccounts.Add(bot);
        db.Channels.Add(Channel(failing, "tw-f", "chan_f", isOnboarded: true));
        db.Channels.Add(Channel(succeeding, "tw-s", "chan_s", isOnboarded: true));
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(failing, "tw-bot-1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure(
                    "Missing required scope 'channel:manage:moderators'.",
                    TwitchErrorCodes.MissingScope
                )
            );
        moderators
            .AddModeratorAsync(succeeding, "tw-bot-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        ListLogger<BotModGrantOnBotAuthorizedHandler> log = new();
        BotModGrantOnBotAuthorizedHandler sut = new(db, moderators, log);

        await sut.HandleAsync(BotAuthorized(bot.Id));

        // The failing channel logged a warning and the sweep still reached the next channel.
        await moderators
            .Received(1)
            .AddModeratorAsync(succeeding, "tw-bot-1", Arg.Any<CancellationToken>());
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task BotBackfill_handler_is_a_noop_when_the_bot_account_row_is_missing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ListLogger<BotModGrantOnBotAuthorizedHandler> log = new();
        BotModGrantOnBotAuthorizedHandler sut = new(db, moderators, log);

        Func<Task> act = () => sut.HandleAsync(BotAuthorized(Guid.NewGuid()));

        await act.Should().NotThrowAsync();
        await moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static BotAccountAuthorizedEvent BotAuthorized(Guid botAccountId) =>
        new()
        {
            BroadcasterId = Guid.Empty,
            BotAccountId = botAccountId,
            IdentityType = AuthEnums.BotIdentityType.Shared,
            BotUsername = "nomnomzbot",
        };

    // ── Default builtin-commands seed handler ─────────────────────────────────

    [Fact]
    public async Task DefaultCommands_handler_seeds_the_five_music_builtins_for_the_event_broadcaster()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        DefaultCommandsSeeder seeder = new(db);
        ListLogger<DefaultCommandsSeedOnOnboardingHandler> log = new();
        DefaultCommandsSeedOnOnboardingHandler sut = new(seeder, log);

        await sut.HandleAsync(Event());

        List<ChannelBuiltinCommand> seeded = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == Broadcaster)
            .ToListAsync();

        seeded.Should().HaveCount(5);
        // BARE keys — the canonical format the dashboard toggle UI queries by (item 24c: bang-prefixed
        // seeded rows were orphaned from the toggle surface).
        seeded
            .Select(c => c.BuiltinKey)
            .Should()
            .BeEquivalentTo(["sr", "skip", "queue", "volume", "song"]);
        seeded.Should().OnlyContain(c => c.IsEnabled);
    }

    [Fact]
    public async Task DefaultCommands_handler_is_idempotent_a_second_run_adds_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        DefaultCommandsSeeder seeder = new(db);
        ListLogger<DefaultCommandsSeedOnOnboardingHandler> log = new();
        DefaultCommandsSeedOnOnboardingHandler sut = new(seeder, log);

        await sut.HandleAsync(Event());
        await sut.HandleAsync(Event());

        (await db.ChannelBuiltinCommands.CountAsync(c => c.BroadcasterId == Broadcaster))
            .Should()
            .Be(5);
    }

    [Fact]
    public async Task DefaultCommands_handler_only_seeds_the_event_broadcaster_not_other_channels()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid otherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        db.Channels.Add(Channel(otherChannel, "tw-999", "someoneelse", isOnboarded: true));
        await db.SaveChangesAsync();

        DefaultCommandsSeeder seeder = new(db);
        ListLogger<DefaultCommandsSeedOnOnboardingHandler> log = new();
        DefaultCommandsSeedOnOnboardingHandler sut = new(seeder, log);

        await sut.HandleAsync(Event());

        (await db.ChannelBuiltinCommands.CountAsync(c => c.BroadcasterId == otherChannel))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task DefaultCommands_handler_catches_and_logs_a_failure_without_throwing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await db.DisposeAsync();

        DefaultCommandsSeeder seeder = new(db);
        ListLogger<DefaultCommandsSeedOnOnboardingHandler> log = new();
        DefaultCommandsSeedOnOnboardingHandler sut = new(seeder, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── EventSub-subscribe seed handler (Slice B) ─────────────────────────────

    [Fact]
    public async Task EventSub_handler_subscribes_the_broadcaster_to_BotLifecycleServices_topic_set()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Broadcaster,
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ListLogger<EventSubSubscribeOnOnboardingHandler> log = new();
        EventSubSubscribeOnOnboardingHandler sut = new(eventSub, log);

        await sut.HandleAsync(Event());

        // Proves the handler reuses BotLifecycleService.ChannelEventTypes verbatim rather than a duplicated
        // (and driftable) copy of the topic list.
        await eventSub
            .Received(1)
            .EnsureSubscribedAsync(
                Broadcaster,
                Arg.Is<IReadOnlyCollection<string>>(types =>
                    types.SequenceEqual(BotLifecycleService.ChannelEventTypes)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task EventSub_handler_logs_a_warning_when_the_subscribe_reconcile_fails()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Twitch request failed (500).", TwitchErrorCodes.TwitchError));

        ListLogger<EventSubSubscribeOnOnboardingHandler> log = new();
        EventSubSubscribeOnOnboardingHandler sut = new(eventSub, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task EventSub_handler_catches_and_logs_an_unexpected_failure_without_throwing()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("transport is down"));

        ListLogger<EventSubSubscribeOnOnboardingHandler> log = new();
        EventSubSubscribeOnOnboardingHandler sut = new(eventSub, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Channel info seed handler (Slice C: content-labels + delay extension) ───

    [Fact]
    public async Task ChannelInfo_handler_seeds_title_game_tags_ccl_and_delay_for_the_event_broadcaster()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChannelInformation(
                        "tw-123",
                        "stoney_eagle",
                        "Stoney_Eagle",
                        "en",
                        "509658",
                        "Just Chatting",
                        "Hello stream",
                        30,
                        ["gaming", "english"],
                        ["DrugsIntoxication", "Gambling"],
                        false
                    )
                )
            );

        ListLogger<ChannelInfoSeedOnOnboardingHandler> log = new();
        ChannelInfoSeedOnOnboardingHandler sut = new(db, channels, log);

        await sut.HandleAsync(Event());

        Channel? updated = await db.Channels.FindAsync(Broadcaster);

        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Hello stream");
        updated.GameName.Should().Be("Just Chatting");
        updated.Tags.Should().BeEquivalentTo(["gaming", "english"]);
        updated.ContentLabels.Should().BeEquivalentTo(["DrugsIntoxication", "Gambling"]);
        updated.StreamDelay.Should().Be(30);
    }

    [Fact]
    public async Task ChannelInfo_handler_is_idempotent_a_second_identical_pull_leaves_the_same_state()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true));
        await db.SaveChangesAsync();

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChannelInformation(
                        "tw-123",
                        "stoney_eagle",
                        "Stoney_Eagle",
                        "en",
                        "509658",
                        "Just Chatting",
                        "Hello stream",
                        30,
                        ["gaming", "english"],
                        ["DrugsIntoxication", "Gambling"],
                        false
                    )
                )
            );

        ListLogger<ChannelInfoSeedOnOnboardingHandler> log = new();
        ChannelInfoSeedOnOnboardingHandler sut = new(db, channels, log);

        await sut.HandleAsync(Event());
        await sut.HandleAsync(Event());

        Channel? updated = await db.Channels.FindAsync(Broadcaster);

        updated!.ContentLabels.Should().BeEquivalentTo(["DrugsIntoxication", "Gambling"]);
        updated.StreamDelay.Should().Be(30);
    }

    [Fact]
    public async Task ChannelInfo_handler_never_clears_existing_content_labels_when_twitch_reports_none()
    {
        // Mirrors the existing Tags guard: an empty CCL response must not wipe a previously-seeded value.
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true);
        channel.ContentLabels = ["Gambling"];
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChannelInformation(
                        "tw-123",
                        "stoney_eagle",
                        "Stoney_Eagle",
                        "en",
                        "509658",
                        "Just Chatting",
                        "Hello stream",
                        0,
                        [],
                        [],
                        false
                    )
                )
            );

        ListLogger<ChannelInfoSeedOnOnboardingHandler> log = new();
        ChannelInfoSeedOnOnboardingHandler sut = new(db, channels, log);

        await sut.HandleAsync(Event());

        Channel? updated = await db.Channels.FindAsync(Broadcaster);
        updated!.ContentLabels.Should().BeEquivalentTo(["Gambling"]);
    }

    [Fact]
    public async Task ChannelInfo_handler_updates_stream_delay_unconditionally_including_down_to_zero()
    {
        // Unlike Tags/CCL, 0 is a legitimate delay value (disabled) rather than "not returned", so the
        // handler must apply it even when it differs from a previously non-zero value.
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = Channel(Broadcaster, "tw-123", "stoney_eagle", isOnboarded: true);
        channel.StreamDelay = 30;
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChannelInformation(
                        "tw-123",
                        "stoney_eagle",
                        "Stoney_Eagle",
                        "en",
                        "509658",
                        "Just Chatting",
                        "Hello stream",
                        0,
                        [],
                        [],
                        false
                    )
                )
            );

        ListLogger<ChannelInfoSeedOnOnboardingHandler> log = new();
        ChannelInfoSeedOnOnboardingHandler sut = new(db, channels, log);

        await sut.HandleAsync(Event());

        Channel? updated = await db.Channels.FindAsync(Broadcaster);
        updated!.StreamDelay.Should().Be(0);
    }

    // ── Chat settings seed handler (Slice C) ─────────────────────────────────

    [Fact]
    public async Task ChatSettings_handler_seeds_the_chat_settings_row_from_twitch_for_the_event_broadcaster()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchChatApi chatApi = Substitute.For<ITwitchChatApi>();
        chatApi
            .GetChatSettingsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChatSettings(
                        Broadcaster.ToString(),
                        EmoteMode: true,
                        FollowerMode: true,
                        FollowerModeDuration: 10,
                        ModeratorId: null,
                        NonModeratorChatDelay: null,
                        NonModeratorChatDelayDuration: null,
                        SlowMode: true,
                        SlowModeWaitTime: 30,
                        SubscriberMode: false,
                        UniqueChatMode: false
                    )
                )
            );

        ListLogger<ChatSettingsSeedOnOnboardingHandler> log = new();
        ChatSettingsSeedOnOnboardingHandler sut = new(db, chatApi, log);

        await sut.HandleAsync(Event());

        Configuration? row = await db.Configurations.FirstOrDefaultAsync(c =>
            c.BroadcasterId == Broadcaster && c.Key == "chat.settings"
        );

        row.Should().NotBeNull();
        row!.Value.Should().Contain("\"slowMode\":true");
        row.Value.Should().Contain("\"slowModeDelay\":30");
        row.Value.Should().Contain("\"subscriberOnly\":false");
        row.Value.Should().Contain("\"emotesOnly\":true");
        row.Value.Should().Contain("\"followersOnly\":true");
        row.Value.Should().Contain("\"followersOnlyDuration\":10");
    }

    [Fact]
    public async Task ChatSettings_handler_is_idempotent_and_never_overwrites_an_existing_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchChatApi chatApi = Substitute.For<ITwitchChatApi>();
        chatApi
            .GetChatSettingsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChatSettings(
                        Broadcaster.ToString(),
                        EmoteMode: false,
                        FollowerMode: false,
                        FollowerModeDuration: 0,
                        ModeratorId: null,
                        NonModeratorChatDelay: null,
                        NonModeratorChatDelayDuration: null,
                        SlowMode: true,
                        SlowModeWaitTime: 30,
                        SubscriberMode: false,
                        UniqueChatMode: false
                    )
                )
            );

        ListLogger<ChatSettingsSeedOnOnboardingHandler> log = new();
        ChatSettingsSeedOnOnboardingHandler sut = new(db, chatApi, log);

        await sut.HandleAsync(Event());

        // The streamer customizes the settings afterwards via the dashboard.
        Configuration row = await db.Configurations.SingleAsync(c =>
            c.BroadcasterId == Broadcaster && c.Key == "chat.settings"
        );
        row.Value =
            "{\"slowMode\":false,\"slowModeDelay\":0,\"subscriberOnly\":true,\"emotesOnly\":false,\"followersOnly\":false,\"followersOnlyDuration\":0}";
        await db.SaveChangesAsync();

        // Twitch now reports different settings on a re-onboard/backfill pass — must not clobber the row.
        chatApi
            .GetChatSettingsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChatSettings(
                        Broadcaster.ToString(),
                        EmoteMode: true,
                        FollowerMode: true,
                        FollowerModeDuration: 99,
                        ModeratorId: null,
                        NonModeratorChatDelay: null,
                        NonModeratorChatDelayDuration: null,
                        SlowMode: false,
                        SlowModeWaitTime: 0,
                        SubscriberMode: false,
                        UniqueChatMode: false
                    )
                )
            );

        await sut.HandleAsync(Event());

        List<Configuration> rows = await db
            .Configurations.Where(c => c.BroadcasterId == Broadcaster && c.Key == "chat.settings")
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows.Single().Value.Should().Contain("\"subscriberOnly\":true");
        rows.Single().Value.Should().NotContain("\"followersOnlyDuration\":99");
    }

    [Fact]
    public async Task ChatSettings_handler_logs_a_warning_and_seeds_nothing_when_the_helix_call_fails()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchChatApi chatApi = Substitute.For<ITwitchChatApi>();
        chatApi
            .GetChatSettingsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchChatSettings>(
                    "Channel is not known locally.",
                    TwitchErrorCodes.NotFound
                )
            );

        ListLogger<ChatSettingsSeedOnOnboardingHandler> log = new();
        ChatSettingsSeedOnOnboardingHandler sut = new(db, chatApi, log);

        await sut.HandleAsync(Event());

        (await db.Configurations.AnyAsync(c => c.BroadcasterId == Broadcaster)).Should().BeFalse();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ChatSettings_handler_catches_and_logs_an_unexpected_failure_without_throwing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchChatApi chatApi = Substitute.For<ITwitchChatApi>();
        chatApi
            .GetChatSettingsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));

        ListLogger<ChatSettingsSeedOnOnboardingHandler> log = new();
        ChatSettingsSeedOnOnboardingHandler sut = new(db, chatApi, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── shared test scaffolding ────────────────────────────────────────────────

    private static Result<UserDto> UserResult(Guid id, string username, string displayName) =>
        Result.Success(
            new UserDto(
                id.ToString(),
                username,
                displayName,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
        );

    private static BotAccount SharedBot() =>
        new()
        {
            Id = Guid.NewGuid(),
            IdentityType = AuthEnums.BotIdentityType.Shared,
            Platform = "twitch",
            BotUserId = "tw-bot-1",
            BotUsername = "nomnomzbot",
            IsActive = true,
        };

    private static Channel Channel(Guid id, string twitchId, string name, bool isOnboarded) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = twitchId,
            Name = name,
            NameNormalized = name,
            IsOnboarded = isOnboarded,
        };

    /// <summary>A scope factory whose every scope resolves the one shared test <see cref="AuthDbContext"/> —
    /// mirrors <c>OnboardedChannelSeedBackfillServiceTests</c>' helper for handlers that create their own
    /// scope (<see cref="IServiceScopeFactory"/>) to isolate a multi-row insert.</summary>
    private sealed class SingleContextScopeFactory(IApplicationDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db);

        private sealed class Scope(IApplicationDbContext db) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IApplicationDbContext) ? db : null;

            public void Dispose() { }
        }
    }

    /// <summary>Simulates the DB being unavailable, so a handler's own-scope creation fails — proving the
    /// catch-and-log contract without needing to corrupt a real context.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("scope unavailable");
    }
}
