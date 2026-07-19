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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
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
            new User
            {
                Id = OwnerId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new Channel
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

    private static (ChannelService Sut, IChannelRegistry Registry, RecordingEventBus Bus) Build(
        AuthDbContext db
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        RecordingEventBus bus = new();
        return (new ChannelService(db, TimeProvider.System, bus, registry), registry, bus);
    }

    [Fact]
    public async Task Get_defaults_to_bang_prefix_and_auto_join_on()
    {
        (ChannelService sut, _, _) = Build(SeededDb());

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
        (ChannelService sut, IChannelRegistry registry, RecordingEventBus bus) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new UpdateChannelSettingsDto
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
        (ChannelService sut, _, _) = Build(db);

        // Only the prefix is supplied; locale/auto-join/timezone are null and must not be overwritten.
        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new UpdateChannelSettingsDto { Prefix = "~" }
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
        (ChannelService sut, IChannelRegistry registry, _) = Build(db);

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new UpdateChannelSettingsDto { Prefix = "a b" }
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
        (ChannelService sut, _, _) = Build(SeededDb());

        Result<ChannelBasicsDto> result = await sut.UpdateBasicsAsync(
            ChannelId.ToString(),
            new UpdateChannelSettingsDto { Prefix = "!!!!!!" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_for_an_unknown_channel_is_not_found()
    {
        (ChannelService sut, _, _) = Build(SeededDb());

        Result<ChannelBasicsDto> result = await sut.GetBasicsAsync(Guid.NewGuid().ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }
}
