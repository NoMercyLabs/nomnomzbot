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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The shared bot grants itself moderator again when it lost the role: the reconcile sweep reads Twitch, and a
/// "not a moderator" answer for the configured shared bot triggers one grant with the broadcaster token, retried
/// no more often than the per-channel window allows.
/// </summary>
public sealed class BotModeratorSelfHealTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
    private const string BotUserId = "1335549269";
    private const string BotLogin = "nomz_bot";

    private readonly AuthDbContext _db = AuthTestBuilder.NewContext();
    private readonly ITwitchModeratorsApi _moderators = Substitute.For<ITwitchModeratorsApi>();
    private readonly IScopeNotificationService _scopeNotices =
        Substitute.For<IScopeNotificationService>();
    private readonly IChannelTwitchBotResolver _resolver =
        Substitute.For<IChannelTwitchBotResolver>();
    private readonly IActionRequiredChangeNotifier _inbox =
        Substitute.For<IActionRequiredChangeNotifier>();
    private readonly FakeTimeProvider _clock = new(new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly BotModeratorGrantBackoff _backoff;

    public BotModeratorSelfHealTests()
    {
        _backoff = new(_clock);
        _resolver
            .ResolveAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(new ChannelTwitchBot(BotUserId, BotLogin));
    }

    [Fact]
    public async Task Reconcile_grants_moderator_when_the_shared_bot_is_not_one_and_records_the_new_standing()
    {
        await SeedAsync();
        BotReadsAsModerator(false);
        _moderators
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await Service("1335549269").ReconcileAsync(Broadcaster);

        result.IsSuccess.Should().BeTrue();
        await _moderators
            .Received(1)
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>());
        Channel stored = await _db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster);
        stored.BotIsModerator.Should().BeTrue();
        stored.BotModeratorStatusBotUserId.Should().Be(BotUserId);
    }

    [Fact]
    public async Task Reconcile_does_not_grant_when_the_bot_already_is_a_moderator()
    {
        await SeedAsync();
        BotReadsAsModerator(true);

        await Service("1335549269").ReconcileAsync(Broadcaster);

        await _moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_records_the_scope_gap_and_keeps_the_status_when_the_grant_scope_is_missing()
    {
        await SeedAsync();
        BotReadsAsModerator(false);
        _moderators
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure(
                    "Missing required scope 'channel:manage:moderators'.",
                    TwitchErrorCodes.MissingScope
                )
            );

        Result result = await Service("1335549269").ReconcileAsync(Broadcaster);

        result.IsSuccess.Should().BeTrue();
        await _scopeNotices
            .Received(1)
            .RecordMissingScopeAsync(
                Broadcaster,
                TwitchScopes.ChannelManageModerators,
                null,
                Arg.Any<CancellationToken>()
            );
        Channel stored = await _db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster);
        stored.BotIsModerator.Should().BeFalse();
    }

    [Fact]
    public async Task Reconcile_does_not_retry_inside_the_backoff_window_and_retries_after_it()
    {
        await SeedAsync();
        BotReadsAsModerator(false);
        _moderators
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Twitch refused.", TwitchErrorCodes.TwitchError));
        BotModeratorStatusService sut = Service("1335549269");

        await sut.ReconcileAsync(Broadcaster);
        _clock.Advance(BotModeratorGrantBackoff.Window - TimeSpan.FromMinutes(1));
        await sut.ReconcileAsync(Broadcaster);

        await _moderators
            .Received(1)
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMinutes(2));
        await sut.ReconcileAsync(Broadcaster);

        await _moderators
            .Received(2)
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_never_grants_a_shared_bot_that_is_not_the_configured_one()
    {
        await SeedAsync();
        BotReadsAsModerator(false);

        await Service("999").ReconcileAsync(Broadcaster);

        await _moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_respects_a_streamer_who_removed_the_bot_and_never_grants_again()
    {
        await SeedAsync();
        BotModeratorStatusService sut = Service("1335549269");

        await sut.ApplyRoleChangeAsync(Broadcaster, BotUserId, isModerator: false);
        BotReadsAsModerator(false);
        await sut.ReconcileAsync(Broadcaster);
        _clock.Advance(BotModeratorGrantBackoff.Window + TimeSpan.FromMinutes(1));
        await sut.ReconcileAsync(Broadcaster);

        await _moderators
            .DidNotReceive()
            .AddModeratorAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Channel stored = await _db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster);
        stored.BotIsModerator.Should().BeFalse();
        stored.BotModeratorRemovedAt.Should().Be(new DateTime(2026, 10, 18, 12, 0, 0));
    }

    [Fact]
    public async Task Reconcile_grants_again_after_the_streamer_added_the_bot_back_and_it_was_lost()
    {
        await SeedAsync();
        BotModeratorStatusService sut = Service("1335549269");
        _moderators
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await sut.ApplyRoleChangeAsync(Broadcaster, BotUserId, isModerator: false);
        await sut.ApplyRoleChangeAsync(Broadcaster, BotUserId, isModerator: true);
        BotReadsAsModerator(false);
        await sut.ReconcileAsync(Broadcaster);

        Channel stored = await _db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster);
        stored.BotModeratorRemovedAt.Should().BeNull();
        await _moderators
            .Received(1)
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Onboarding_again_clears_the_removal_so_the_self_heal_can_grant()
    {
        await SeedAsync();
        BotModeratorStatusService sut = Service("1335549269");
        await sut.ApplyRoleChangeAsync(Broadcaster, BotUserId, isModerator: false);
        BotJoinOnOnboardingHandler onboarding = new(
            _db,
            _moderators,
            Pinned("1335549269"),
            TestSanction.Held(),
            NullLogger<BotJoinOnOnboardingHandler>.Instance
        );
        _moderators
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Twitch refused.", TwitchErrorCodes.TwitchError));

        await onboarding.HandleAsync(
            new ChannelOnboardedEvent
            {
                BroadcasterId = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "tw-streamer-1",
                Name = "streamer",
            }
        );
        BotReadsAsModerator(false);
        await sut.ReconcileAsync(Broadcaster);

        Channel stored = await _db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster);
        stored.BotModeratorRemovedAt.Should().BeNull();
        await _moderators
            .Received(2)
            .AddModeratorAsync(Broadcaster, BotUserId, Arg.Any<CancellationToken>());
    }

    private static IConfiguration Pinned(string pinnedBotUserId) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Twitch:BotUserId"] = pinnedBotUserId }
            )
            .Build();

    private void BotReadsAsModerator(bool isModerator) =>
        _moderators
            .GetModeratorsByUserIdAsync(
                Broadcaster,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        isModerator ? [new(BotUserId, BotLogin, BotLogin)] : [],
                        null,
                        0
                    )
                )
            );

    private async Task SeedAsync()
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
        _db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "tw-streamer-1",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
            }
        );
        await _db.SaveChangesAsync();
    }

    private BotModeratorStatusService Service(string pinnedBotUserId)
    {
        BotModeratorGrantService grants = new(
            _db,
            _moderators,
            _scopeNotices,
            Pinned(pinnedBotUserId),
            TestSanction.Held(),
            _backoff,
            NullLogger<BotModeratorGrantService>.Instance
        );
        return new(_db, _resolver, _moderators, grants, _inbox, _clock);
    }
}
