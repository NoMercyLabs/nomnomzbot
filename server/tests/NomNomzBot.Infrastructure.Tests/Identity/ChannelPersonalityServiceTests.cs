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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the channel personality-tone endpoint contract end to end through <see cref="ChannelService"/>:
/// the default is Informative, a valid tone round-trips (persisted + registry refreshed + change fanned out),
/// an invalid tone is rejected without a write, and an unknown channel is a not-found.
/// </summary>
public sealed class ChannelPersonalityServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0198d000-0000-7000-8000-0000000000e1");
    private static readonly Guid OwnerId = Guid.Parse("0198d000-0000-7000-8000-0000000000e2");

    private static AuthDbContext SeededDb()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
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
    public async Task Get_defaults_to_Informative_and_lists_every_selectable_tone()
    {
        (ChannelService sut, _, _) = Build(SeededDb());

        Result<ChannelPersonalityDto> result = await sut.GetPersonalityAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Personality.Should().Be(PersonalityTone.Informative);
        result.Value.Available.Should().BeEquivalentTo(PersonalityTone.All);
    }

    [Fact]
    public async Task Set_a_valid_tone_persists_refreshes_the_registry_and_fans_the_change_out()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, IChannelRegistry registry, RecordingEventBus bus) = Build(db);

        Result<ChannelPersonalityDto> result = await sut.SetPersonalityAsync(
            ChannelId.ToString(),
            PersonalityTone.Sassy
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Personality.Should().Be(PersonalityTone.Sassy);

        // Persisted: the row now carries the new tone.
        Channel? saved = await db.Channels.FindAsync(ChannelId);
        saved!.Personality.Should().Be(PersonalityTone.Sassy);

        // Registry refreshed so the live chat hot path picks up the new tone without a restart.
        await registry.Received(1).InvalidateSettingsAsync(ChannelId, Arg.Any<CancellationToken>());

        // Change fanned out for other consumers (dashboard live push).
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == ChannelId
                && e.Domain == "channel-settings"
                && e.EntityId == "personality"
                && e.Action == "updated"
            );
    }

    [Fact]
    public async Task Set_normalizes_case_to_the_canonical_token()
    {
        (ChannelService sut, _, _) = Build(SeededDb());

        Result<ChannelPersonalityDto> result = await sut.SetPersonalityAsync(
            ChannelId.ToString(),
            "HYPE"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Personality.Should().Be(PersonalityTone.Hype);
    }

    [Fact]
    public async Task Set_an_unknown_tone_is_rejected_and_does_not_write()
    {
        AuthDbContext db = SeededDb();
        (ChannelService sut, IChannelRegistry registry, _) = Build(db);

        Result<ChannelPersonalityDto> result = await sut.SetPersonalityAsync(
            ChannelId.ToString(),
            "grumpy"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");

        Channel? saved = await db.Channels.FindAsync(ChannelId);
        saved!
            .Personality.Should()
            .Be(PersonalityTone.Informative, "an invalid tone must not overwrite");
        await registry.DidNotReceiveWithAnyArgs().InvalidateSettingsAsync(default, default);
    }

    [Fact]
    public async Task Get_for_an_unknown_channel_is_not_found()
    {
        (ChannelService sut, _, _) = Build(SeededDb());

        Result<ChannelPersonalityDto> result = await sut.GetPersonalityAsync(
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }
}
