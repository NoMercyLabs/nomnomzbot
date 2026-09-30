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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.EventHandlers;
using NomNomzBot.Infrastructure.Notifications;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// The "bot is not a moderator" inbox item must be true at all times (plan item A0). Twitch's moderator role
/// changes naming the bot, and a Helix Get Moderators reconcile, drive the channel's recorded bot status; the
/// inbox item appears and clears with it, and a change for anyone else never touches it.
/// </summary>
public sealed class BotNotModeratorTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000b1");
    private const string BroadcasterTwitchId = "100";
    private const string BotTwitchId = "900";
    private const string BotLogin = "nomz_bot";
    private static readonly DateTime T0 = new(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_bot_losing_moderator_records_the_status_and_raises_the_item()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: true);

        await f.Handler.HandleAsync(Removed(BotTwitchId));

        Channel channel = await f.ChannelAsync();
        channel.BotIsModerator.Should().BeFalse();
        channel.BotModeratorStatusBotUserId.Should().Be(BotTwitchId);
        channel.BotModeratorStatusChangedAt.Should().Be(T0);

        ActionRequiredItemDto item = (await f.ItemsAsync()).Should().ContainSingle().Subject;
        item.Id.Should().Be($"bot-not-moderator:{ChannelId}:{T0.Ticks}");
        item.Kind.Should().Be("bot_not_moderator");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_bot_not_moderator_title");
        item.MessageKey.Should().Be("attention_bot_not_moderator_message");
        item.Parameters.Should().Contain("botName", BotLogin);
        item.DeepLinkRoute.Should().Be("moderation");
        item.DetectedAt.Should().Be(T0);
        f.Notifier.Signalled.Should().Equal(ChannelId);
    }

    [Fact]
    public async Task The_bot_regaining_moderator_resolves_the_item_live()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: true);
        await f.Handler.HandleAsync(Removed(BotTwitchId));
        (await f.ItemsAsync()).Should().ContainSingle();

        f.Clock.Advance(TimeSpan.FromMinutes(3));
        await f.Handler.HandleAsync(Added(BotTwitchId));

        Channel channel = await f.ChannelAsync();
        channel.BotIsModerator.Should().BeTrue();
        channel.BotModeratorStatusChangedAt.Should().Be(T0.AddMinutes(3));
        (await f.ItemsAsync()).Should().BeEmpty();
        f.Notifier.Signalled.Should().Equal(ChannelId, ChannelId);
    }

    [Fact]
    public async Task A_moderator_change_for_another_user_leaves_the_bot_status_alone()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: true);

        await f.Handler.HandleAsync(Removed("555"));

        Channel channel = await f.ChannelAsync();
        channel.BotIsModerator.Should().BeTrue();
        channel.BotModeratorStatusChangedAt.Should().Be(Fixture.SeededChangedAt);
        (await f.ItemsAsync()).Should().BeEmpty();
        f.Notifier.Signalled.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeing_the_same_removal_again_keeps_one_item_with_one_identity()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: true);
        await f.Handler.HandleAsync(Removed(BotTwitchId));
        string firstId = (await f.ItemsAsync()).Single().Id;

        f.Clock.Advance(TimeSpan.FromMinutes(5));
        await f.Handler.HandleAsync(Removed(BotTwitchId));

        (await f.ChannelAsync()).BotModeratorStatusChangedAt.Should().Be(T0);
        (await f.ItemsAsync()).Should().ContainSingle().Which.Id.Should().Be(firstId);
        f.Notifier.Signalled.Should().Equal(ChannelId);
    }

    [Fact]
    public async Task A_dismissed_item_stays_hidden_until_the_bot_is_demodded_again()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: true);
        await f.Handler.HandleAsync(Removed(BotTwitchId));
        string firstId = (await f.ItemsAsync()).Single().Id;

        await f.Inbox.DismissAsync(ChannelId, Guid.NewGuid(), [firstId]);
        (await f.ItemsAsync()).Should().BeEmpty();

        f.Clock.Advance(TimeSpan.FromHours(1));
        await f.Handler.HandleAsync(Added(BotTwitchId));
        f.Clock.Advance(TimeSpan.FromHours(1));
        await f.Handler.HandleAsync(Removed(BotTwitchId));

        (await f.ItemsAsync())
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be($"bot-not-moderator:{ChannelId}:{T0.AddHours(2).Ticks}");
    }

    [Fact]
    public async Task Reconcile_with_Twitch_not_listing_the_bot_raises_the_item()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: null);
        f.ModeratorsSays();

        Result result = await f.Status.ReconcileAsync(ChannelId);

        result.IsSuccess.Should().BeTrue();
        await f
            .Moderators.Received(1)
            .GetModeratorsByUserIdAsync(
                ChannelId,
                Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { BotTwitchId })),
                Arg.Any<CancellationToken>()
            );
        (await f.ChannelAsync()).BotIsModerator.Should().BeFalse();
        (await f.ItemsAsync()).Should().ContainSingle().Which.Kind.Should().Be("bot_not_moderator");
        f.Notifier.Signalled.Should().Equal(ChannelId);
    }

    [Fact]
    public async Task Reconcile_with_Twitch_listing_the_bot_resolves_the_item()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: false);
        (await f.ItemsAsync()).Should().ContainSingle();
        f.ModeratorsSays(BotTwitchId);

        (await f.Status.ReconcileAsync(ChannelId)).IsSuccess.Should().BeTrue();

        (await f.ChannelAsync()).BotIsModerator.Should().BeTrue();
        (await f.ItemsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_roster_is_inconclusive_and_changes_nothing()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: false);
        f.Moderators.GetModeratorsByUserIdAsync(
                ChannelId,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchModerator>>(
                    "Missing required scope 'moderation:read'.",
                    TwitchErrorCodes.MissingScope
                )
            );

        Result result = await f.Status.ReconcileAsync(ChannelId);

        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        Channel channel = await f.ChannelAsync();
        channel.BotIsModerator.Should().BeFalse();
        channel.BotModeratorStatusChangedAt.Should().Be(Fixture.SeededChangedAt);
        (await f.ItemsAsync()).Should().ContainSingle();
        f.Notifier.Signalled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_status_observed_for_a_replaced_bot_raises_nothing()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: false);
        BotAccount custom = new()
        {
            IdentityType = "custom",
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = "901",
            BotUsername = "my_own_bot",
            ConnectionId = Guid.NewGuid(),
        };
        f.Db.BotAccounts.Add(custom);
        f.Db.ChannelBotAuthorizations.Add(
            new()
            {
                BroadcasterId = ChannelId,
                BotAccountId = custom.Id,
                AuthorizedAt = T0,
            }
        );
        await f.Db.SaveChangesAsync();

        (await f.ItemsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Reconcile_without_a_dedicated_bot_clears_the_status()
    {
        await using Fixture f = await Fixture.CreateAsync(botIsModerator: false);
        BotAccount shared = await f.Db.BotAccounts.SingleAsync();
        shared.IsActive = false;
        await f.Db.SaveChangesAsync();

        (await f.Status.ReconcileAsync(ChannelId)).IsSuccess.Should().BeTrue();

        Channel channel = await f.ChannelAsync();
        channel.BotIsModerator.Should().BeNull();
        channel.BotModeratorStatusBotUserId.Should().BeNull();
        channel.BotModeratorStatusChangedAt.Should().BeNull();
        (await f.ItemsAsync()).Should().BeEmpty();
        await f.Moderators.DidNotReceiveWithAnyArgs().GetModeratorsByUserIdAsync(default, default!);
    }

    private static ModeratorRemovedEvent Removed(string twitchUserId) =>
        new()
        {
            BroadcasterId = ChannelId,
            UserId = twitchUserId,
            UserLogin = "someone",
            UserDisplayName = "Someone",
        };

    private static ModeratorAddedEvent Added(string twitchUserId) =>
        new()
        {
            BroadcasterId = ChannelId,
            UserId = twitchUserId,
            UserLogin = "someone",
            UserDisplayName = "Someone",
        };

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTime SeededChangedAt = T0.AddDays(-1);

        private Fixture(ActionRequiredInboxServiceTestDbContext db)
        {
            Db = db;
            Status = new(db, new ChannelTwitchBotResolver(db), Moderators, Notifier, Clock);
            Handler = new(Status, NullLogger<BotModeratorRoleChangeHandler>.Instance);
            Inbox = ActionRequiredInboxHarness.Create(db, Clock, new RecordingChangeNotifier());
        }

        public ActionRequiredInboxServiceTestDbContext Db { get; }
        public FakeTimeProvider Clock { get; } = new(T0);
        public RecordingChangeNotifier Notifier { get; } = new();
        public ITwitchModeratorsApi Moderators { get; } = Substitute.For<ITwitchModeratorsApi>();
        public BotModeratorStatusService Status { get; }
        public BotModeratorRoleChangeHandler Handler { get; }
        public ActionRequiredInboxService Inbox { get; }

        public static async Task<Fixture> CreateAsync(bool? botIsModerator)
        {
            ActionRequiredInboxServiceTestDbContext db =
                ActionRequiredInboxServiceTestDbContext.New();
            db.Channels.Add(
                new()
                {
                    Id = ChannelId,
                    Name = "streamer",
                    NameNormalized = "streamer",
                    TwitchChannelId = BroadcasterTwitchId,
                    BotIsModerator = botIsModerator,
                    BotModeratorStatusBotUserId = botIsModerator is null ? null : BotTwitchId,
                    BotModeratorStatusChangedAt = botIsModerator is null ? null : SeededChangedAt,
                }
            );
            db.BotAccounts.Add(
                new()
                {
                    IdentityType = AuthEnums.BotIdentityType.Shared,
                    Platform = AuthEnums.Platform.Twitch,
                    BotUserId = BotTwitchId,
                    BotUsername = BotLogin,
                    ConnectionId = Guid.NewGuid(),
                }
            );
            await db.SaveChangesAsync();
            return new(db);
        }

        public void ModeratorsSays(params string[] moderatorTwitchIds) =>
            Moderators
                .GetModeratorsByUserIdAsync(
                    ChannelId,
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Result.Success(
                        new TwitchPage<TwitchModerator>(
                            [.. moderatorTwitchIds.Select(id => new TwitchModerator(id, id, id))],
                            null,
                            moderatorTwitchIds.Length
                        )
                    )
                );

        public async Task<Channel> ChannelAsync() =>
            await Db.Channels.AsNoTracking().SingleAsync(c => c.Id == ChannelId);

        public async Task<List<ActionRequiredItemDto>> ItemsAsync() =>
            (await Inbox.GetItemsAsync(ChannelId)).Value;

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
