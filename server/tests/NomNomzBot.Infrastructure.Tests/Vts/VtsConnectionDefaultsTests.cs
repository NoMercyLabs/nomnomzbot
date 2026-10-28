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
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Infrastructure.Vts;
using NomNomzBot.Infrastructure.Vts.Transport;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Vts;

/// <summary>
/// Every VTube Studio connection setting has one default (<see cref="VtsConnectionDefaults"/>). A channel
/// with no saved row reads those defaults from the API and the read writes nothing; every settable field
/// is saved and read back; the endpoint and the event mask are validated.
/// </summary>
public sealed class VtsConnectionDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fad1");

    private static VtsConnectionService Build(VtsTestDbContext db) =>
        new(db, Substitute.For<ITokenProtector>());

    private static bool IsValid(UpsertVtsConnectionRequest request) =>
        Validator.TryValidateObject(request, new(request), [], validateAllProperties: true);

    [Fact]
    public async Task A_channel_with_no_saved_row_reads_every_default_and_writes_nothing()
    {
        VtsTestDbContext db = VtsTestDbContext.New();

        VtsConnectionDto dto = (await Build(db).GetAsync(Channel)).Value;

        dto.Mode.Should().Be(VtsConnectionDefaults.Mode);
        dto.Endpoint.Should().Be(VtsConnectionDefaults.Endpoint);
        dto.EventSubscriptionsMask.Should().Be(VtsConnectionDefaults.EventSubscriptionsMask);
        dto.IsEnabled.Should().Be(VtsConnectionDefaults.IsEnabled);
        dto.Status.Should().Be(VtsConnectionDefaults.Status);
        dto.HasPluginToken.Should().BeFalse();
        dto.HasBridgeToken.Should().BeFalse();
        (await db.VtsConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void The_entity_initializers_come_from_the_single_source()
    {
        VtsConnection entity = new();

        entity.Mode.Should().Be(VtsConnectionDefaults.Mode);
        entity.Endpoint.Should().Be(VtsConnectionDefaults.Endpoint);
        entity.EventSubscriptionsMask.Should().Be(VtsConnectionDefaults.EventSubscriptionsMask);
        entity.IsEnabled.Should().Be(VtsConnectionDefaults.IsEnabled);
        entity.Status.Should().Be(VtsConnectionDefaults.Status);
    }

    [Fact]
    public async Task A_blank_endpoint_saves_as_the_default()
    {
        VtsTestDbContext db = VtsTestDbContext.New();

        VtsConnectionDto dto = (
            await Build(db).UpsertAsync(Channel, new() { Mode = "direct", Endpoint = " " })
        ).Value;

        dto.Endpoint.Should().Be(VtsConnectionDefaults.Endpoint);
    }

    [Fact]
    public async Task Every_settable_field_is_saved_and_read_back_changed()
    {
        VtsTestDbContext db = VtsTestDbContext.New();
        VtsConnectionService service = Build(db);

        await service.UpsertAsync(
            Channel,
            new()
            {
                Mode = "bridge",
                Endpoint = "ws://192.168.2.50:8001",
                EventSubscriptionsMask = 0x5,
                IsEnabled = true,
            }
        );
        VtsConnectionDto read = (await service.GetAsync(Channel)).Value;

        read.Mode.Should().Be("bridge").And.NotBe(VtsConnectionDefaults.Mode);
        read.Endpoint.Should().Be("ws://192.168.2.50:8001");
        read.EventSubscriptionsMask.Should().Be(0x5);
        read.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void The_largest_event_mask_covers_exactly_the_known_events()
    {
        VtsConnectionDefaults
            .MaxEventSubscriptionsMask.Should()
            .Be((1 << DirectVtsTransport.EventBits.Length) - 1);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(VtsConnectionDefaults.MaxEventSubscriptionsMask, true)]
    [InlineData(VtsConnectionDefaults.MaxEventSubscriptionsMask + 1, false)]
    public void The_event_mask_is_bounded(int mask, bool valid) =>
        IsValid(new() { Mode = "direct", EventSubscriptionsMask = mask }).Should().Be(valid);

    [Theory]
    [InlineData("ws://localhost:8001", true)]
    [InlineData("wss://vts.example.com:8001", true)]
    [InlineData("http://localhost:8001", false)]
    [InlineData("localhost:8001", false)]
    public void The_endpoint_must_be_a_websocket_address(string endpoint, bool valid) =>
        IsValid(new() { Mode = "direct", Endpoint = endpoint }).Should().Be(valid);
}
