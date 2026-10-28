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

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Every OBS connection setting has one default (<see cref="ObsConnectionDefaults"/>). A channel with no
/// saved row reads those defaults from the API and the read writes nothing; every settable field is saved
/// and read back; the event mask is bounded.
/// </summary>
public sealed class ObsConnectionDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f6d1");

    private static ObsConnectionService Build(ObsTestDbContext db) =>
        new(db, Substitute.For<ITokenProtector>());

    [Fact]
    public async Task A_channel_with_no_saved_row_reads_every_default_and_writes_nothing()
    {
        ObsTestDbContext db = ObsTestDbContext.New();

        ObsConnectionDto dto = (await Build(db).GetAsync(Channel)).Value;

        dto.Mode.Should().Be(ObsConnectionDefaults.Mode);
        dto.Host.Should().Be(ObsConnectionDefaults.Host);
        dto.Port.Should().Be(ObsConnectionDefaults.Port);
        dto.EventSubscriptionsMask.Should().Be(ObsConnectionDefaults.EventSubscriptionsMask);
        dto.IsEnabled.Should().Be(ObsConnectionDefaults.IsEnabled);
        dto.HasPassword.Should().BeFalse();
        dto.HasBridgeToken.Should().BeFalse();
        (await db.ObsConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void The_entity_initializers_come_from_the_single_source()
    {
        ObsConnection entity = new();

        entity.Mode.Should().Be(ObsConnectionDefaults.Mode);
        entity.Host.Should().Be(ObsConnectionDefaults.Host);
        entity.Port.Should().Be(ObsConnectionDefaults.Port);
        entity.EventSubscriptionsMask.Should().Be(ObsConnectionDefaults.EventSubscriptionsMask);
        entity.IsEnabled.Should().Be(ObsConnectionDefaults.IsEnabled);
    }

    [Fact]
    public async Task A_blank_host_and_a_missing_port_save_as_the_defaults()
    {
        ObsTestDbContext db = ObsTestDbContext.New();

        ObsConnectionDto dto = (
            await Build(db).UpsertAsync(Channel, new() { Mode = "direct", Host = "  " })
        ).Value;

        dto.Host.Should().Be(ObsConnectionDefaults.Host);
        dto.Port.Should().Be(ObsConnectionDefaults.Port);
        dto.EventSubscriptionsMask.Should().Be(ObsConnectionDefaults.EventSubscriptionsMask);
    }

    [Fact]
    public async Task Every_settable_field_is_saved_and_read_back_changed()
    {
        ObsTestDbContext db = ObsTestDbContext.New();
        ObsConnectionService service = Build(db);

        await service.UpsertAsync(
            Channel,
            new()
            {
                Mode = "bridge",
                Host = "192.168.2.50",
                Port = 4466,
                EventSubscriptionsMask = 0x3,
                IsEnabled = true,
            }
        );
        ObsConnectionDto read = (await service.GetAsync(Channel)).Value;

        read.Mode.Should().Be("bridge").And.NotBe(ObsConnectionDefaults.Mode);
        read.Host.Should().Be("192.168.2.50");
        read.Port.Should().Be(4466);
        read.EventSubscriptionsMask.Should().Be(0x3);
        read.IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(ObsConnectionDefaults.MaxEventSubscriptionsMask, true)]
    [InlineData(ObsConnectionDefaults.MaxEventSubscriptionsMask + 1, false)]
    public void The_event_mask_is_bounded(int mask, bool valid)
    {
        UpsertObsConnectionRequest request = new()
        {
            Mode = "direct",
            EventSubscriptionsMask = mask,
        };

        Validator
            .TryValidateObject(request, new(request), [], validateAllProperties: true)
            .Should()
            .Be(valid);
    }
}
