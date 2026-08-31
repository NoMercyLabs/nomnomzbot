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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the channel "basics" endpoint contract end to end through <see cref="ChannelService"/>: the defaults
/// (prefix "!", auto-join on), a valid update round-trips (prefix + locale + auto-join persisted on the channel,
/// timezone on the owner, registry refreshed, change fanned out), an invalid prefix is rejected without a write,
/// and an unknown channel is a not-found.
/// </summary>
public sealed class ChannelBasicsServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0198d000-0000-7000-8000-0000000000f1");
    private static readonly Guid OwnerId = Guid.Parse("0198d000-0000-7000-8000-0000000000f2");

    private static AuthDbContext SeededDb()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = OwnerId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = OwnerId,
                TwitchChannelId = "tw-owner",
                ExternalChannelId = "tw-owner",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static (
        ChannelService Sut,
        IChannelRegistry Registry,
        RecordingEventBus Bus,
        ITwitchEventSubService EventSub,
        IChatProvider ChatProvider
    ) Build(AuthDbContext db)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        RecordingEventBus bus = new();
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        eventSub
            .UnsubscribeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IChatProvider chatProvider = Substitute.For<IChatProvider>();
        chatProvider
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Task.FromResult(callInfo.Arg<BuiltinResponseRequest>().NeutralFallback)
            );
        return (
            new(db, TimeProvider.System, bus, registry, eventSub, chatProvider, composer),
            registry,
            bus,
            eventSub,
            chatProvider
        );
    }

    [Fact]
    public async Task Get_defaults_to_bang_prefix_and_auto_join_on()
    {
        (ChannelService sut, _, _, _, _) = Build(SeededDb());

        Result<ChannelBasicsDto> result = await sut.GetBasicsAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Prefix.Should().Be("!");
        result.Value.AutoJoin.Should().BeTrue();
        result.Value.Locale.Should().BeNull();
        result.Value.Timezone.Should().BeNull();
    }

    [Fact]
    public async Task Update_persists_every_field_refreshes_the_registry_and_fans_the_change_out()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, IChannelRegistry registry, RecordingEventBus bus, _, _) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new()
            {
                Prefix = "?",
                Locale = "nl",
                AutoJoin = false,
                Timezone = "Europe/Amsterdam",
            }
        );

        // Echoed the saved values.
        result.IsSuccess.Should().BeTrue();
        result.Value.Prefix.Should().Be("?");
        result.Value.Locale.Should().Be("nl");
        result.Value.AutoJoin.Should().BeFalse();
        result.Value.Timezone.Should().Be("Europe/Amsterdam");

        // Persisted: the channel row carries prefix/locale/auto-join; the owner row carries the timezone.
        Channel? channel = await db.Channels.FindAsync(ChannelId);
        channel!.CommandPrefix.Should().Be("?");
        channel.Language.Should().Be("nl");
        channel.Enabled.Should().BeFalse();
        User? owner = await db.Users.FindAsync(OwnerId);
        owner!.Timezone.Should().Be("Europe/Amsterdam");

        // Registry refreshed so the live chat hot path picks up the new prefix without a restart.
        await registry.Received(1).InvalidateSettingsAsync(ChannelId, Arg.Any<CancellationToken>());

        // Change fanned out for other consumers (dashboard live push).
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == ChannelId
                && e.Domain == "channel-settings"
                && e.Action == "updated"
            );
    }

    [Fact]
    public async Task Update_leaves_untouched_fields_unchanged_when_null()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, _, _) = Build(db);

        // Only the prefix is supplied; locale/auto-join/timezone are null and must not be overwritten.
        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { Prefix = "~" }
        );

        result.IsSuccess.Should().BeTrue();
        Channel? channel = await db.Channels.FindAsync(ChannelId);
        channel!.CommandPrefix.Should().Be("~");
        channel.Enabled.Should().BeTrue("a null AutoJoin must not flip the existing value");
        channel.Language.Should().BeNull("a null Locale must not overwrite");
    }

    [Fact]
    public async Task Update_with_a_whitespace_prefix_is_rejected_and_does_not_write()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, IChannelRegistry registry, _, _, _) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { Prefix = "a b" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");

        Channel? channel = await db.Channels.FindAsync(ChannelId);
        channel!.CommandPrefix.Should().Be("!", "an invalid prefix must not overwrite");
        await registry.DidNotReceiveWithAnyArgs().InvalidateSettingsAsync(default, default);
    }

    [Fact]
    public async Task Update_with_an_over_long_prefix_is_rejected()
    {
        (ChannelService sut, _, _, _, _) = Build(SeededDb());

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { Prefix = "!!!!!!" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_for_an_unknown_channel_is_not_found()
    {
        (ChannelService sut, _, _, _, _) = Build(SeededDb());

        Result<ChannelBasicsDto> result = await sut.GetBasicsAsync(Guid.NewGuid().ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }

    [Fact]
    public async Task Update_toggling_auto_join_off_unsubscribes_the_channel_live_without_a_restart()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, ITwitchEventSubService eventSub, _) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { AutoJoin = false }
        );

        result.IsSuccess.Should().BeTrue();
        await eventSub.Received(1).UnsubscribeAllAsync(ChannelId, Arg.Any<CancellationToken>());
        await eventSub.DidNotReceiveWithAnyArgs().EnsureSubscribedAsync(default, default!, default);
    }

    [Fact]
    public async Task Update_toggling_auto_join_on_subscribes_the_channel_live_without_a_restart()
    {
        AuthDbContext db = SeededDb();
        Channel channel = await db.Channels.SingleAsync(c => c.Id == ChannelId);
        channel.Enabled = false;
        await db.SaveChangesAsync();
        (ChannelService sut, _, _, ITwitchEventSubService eventSub, _) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { AutoJoin = true }
        );

        result.IsSuccess.Should().BeTrue();
        await eventSub
            .Received(1)
            .EnsureSubscribedAsync(
                ChannelId,
                BotLifecycleService.ChannelEventTypes,
                Arg.Any<CancellationToken>()
            );
        await eventSub.DidNotReceiveWithAnyArgs().UnsubscribeAllAsync(default, default);
    }

    [Fact]
    public async Task Update_with_auto_join_unchanged_does_not_touch_the_live_eventsub_state()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, ITwitchEventSubService eventSub, _) = Build(db);

        // AutoJoin is already true on the seeded channel — re-sending true must not re-trigger a live subscribe.
        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new() { AutoJoin = true }
        );

        result.IsSuccess.Should().BeTrue();
        await eventSub.DidNotReceiveWithAnyArgs().EnsureSubscribedAsync(default, default!, default);
        await eventSub.DidNotReceiveWithAnyArgs().UnsubscribeAllAsync(default, default);
    }

    [Fact]
    public async Task JoinAsync_subscribes_the_channel_to_eventsub_live()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, ITwitchEventSubService eventSub, _) = Build(db);

        Result result = await sut.JoinAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        await eventSub
            .Received(1)
            .EnsureSubscribedAsync(
                ChannelId,
                BotLifecycleService.ChannelEventTypes,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task JoinAsync_with_announce_on_connect_enabled_sends_the_tone_resolved_chat_message()
    {
        AuthDbContext db = SeededDb();
        Channel channel = await db.Channels.SingleAsync(c => c.Id == ChannelId);
        channel.AnnounceOnConnect = true;
        await db.SaveChangesAsync();
        (ChannelService sut, _, _, _, IChatProvider chatProvider) = Build(db);

        Result result = await sut.JoinAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        await chatProvider
            .Received(1)
            .SendMessageAsync(
                ChannelId,
                "I'm now active in this channel!",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task JoinAsync_with_announce_on_connect_left_at_its_default_off_sends_nothing()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, _, IChatProvider chatProvider) = Build(db);

        Result result = await sut.JoinAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        (await db.Channels.SingleAsync(c => c.Id == ChannelId))
            .AnnounceOnConnect.Should()
            .BeFalse();
        await chatProvider.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task LeaveAsync_unsubscribes_the_channel_from_eventsub_live()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, _, _, ITwitchEventSubService eventSub, _) = Build(db);

        Result result = await sut.LeaveAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        await eventSub.Received(1).UnsubscribeAllAsync(ChannelId, Arg.Any<CancellationToken>());
    }
}
