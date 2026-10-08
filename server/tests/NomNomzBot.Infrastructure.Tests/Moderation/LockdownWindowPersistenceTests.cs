// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// A lockdown window survives a restart with everything a restore needs (spam-defense.md §L5.1): the
/// controls it engaged, the value each one had before, the timestamps, and the controls it could not
/// put back. Each read goes through a FRESH DbContext, because the point of persisting is that the
/// process that engaged the window may be gone.
/// </summary>
public class LockdownWindowPersistenceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherChannel = Guid.Parse("0199c000-0000-7000-8000-0000000000a2");

    private static readonly DateTime StartedAt = new(2026, 10, 8, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = StartedAt.AddMinutes(15);
    private static readonly DateTime EndedAt = StartedAt.AddMinutes(4);

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SqliteConnection _connection;

    public LockdownWindowPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext(null);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
    }

    private AppDbContext NewDbContext(Guid? tenant)
    {
        ICurrentTenantService currentTenant = Substitute.For<ICurrentTenantService>();
        currentTenant.BroadcasterId.Returns(tenant);
        currentTenant.HasTenant.Returns(tenant is not null);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options,
            currentTenant
        );
    }

    private static LockdownWindowRecord NewWindow(Guid broadcasterId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            Platform = "twitch",
            Trigger = "hate raid: 40 fresh accounts posted one phrase",
            StartedAt = StartedAt,
            ExpiresAt = ExpiresAt,
            EndedAt = EndedAt,
            EngagedControlsJson = JsonSerializer.Serialize(
                new List<EngagedControl>
                {
                    new(LockdownControl.SlowMode, "off"),
                    new(LockdownControl.FollowersOnly, "30m"),
                },
                Json
            ),
            UnavailableControlsJson = JsonSerializer.Serialize(
                new List<LockdownControl> { LockdownControl.BlockedTerms },
                Json
            ),
            RestorationFailedControlsJson = JsonSerializer.Serialize(
                new List<LockdownControl> { LockdownControl.FollowersOnly },
                Json
            ),
        };

    [Fact]
    public async Task A_saved_window_reads_back_with_its_controls_previous_values_and_timestamps()
    {
        LockdownWindowRecord saved = NewWindow(Channel);
        await using (AppDbContext write = NewDbContext(null))
        {
            write.LockdownWindows.Add(saved);
            await write.SaveChangesAsync();
        }

        await using AppDbContext read = NewDbContext(Channel);
        LockdownWindowRecord loaded = await read.LockdownWindows.SingleAsync(w => w.Id == saved.Id);

        loaded.BroadcasterId.Should().Be(Channel);
        loaded.Platform.Should().Be("twitch");
        loaded.Trigger.Should().Be("hate raid: 40 fresh accounts posted one phrase");
        loaded.StartedAt.Should().Be(StartedAt);
        loaded.ExpiresAt.Should().Be(ExpiresAt);
        loaded.EndedAt.Should().Be(EndedAt);
        loaded.RestoredAt.Should().BeNull("nothing was restored on the platform yet");

        List<EngagedControl> engaged = JsonSerializer.Deserialize<List<EngagedControl>>(
            loaded.EngagedControlsJson,
            Json
        )!;
        engaged
            .Should()
            .Equal(
                new EngagedControl(LockdownControl.SlowMode, "off"),
                new EngagedControl(LockdownControl.FollowersOnly, "30m")
            );
        JsonSerializer
            .Deserialize<List<LockdownControl>>(loaded.UnavailableControlsJson, Json)
            .Should()
            .Equal(LockdownControl.BlockedTerms);
        JsonSerializer
            .Deserialize<List<LockdownControl>>(loaded.RestorationFailedControlsJson, Json)
            .Should()
            .Equal(LockdownControl.FollowersOnly);
    }

    [Fact]
    public async Task A_window_is_invisible_to_another_broadcasters_tenant_scope()
    {
        LockdownWindowRecord saved = NewWindow(Channel);
        await using (AppDbContext write = NewDbContext(null))
        {
            write.LockdownWindows.Add(saved);
            await write.SaveChangesAsync();
        }

        await using AppDbContext other = NewDbContext(OtherChannel);
        (await other.LockdownWindows.ToListAsync())
            .Should()
            .BeEmpty("a lockdown window belongs to one broadcaster and must not leak to another");

        await using AppDbContext owner = NewDbContext(Channel);
        (await owner.LockdownWindows.ToListAsync())
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(saved.Id);
    }

    public void Dispose() => _connection.Dispose();
}
