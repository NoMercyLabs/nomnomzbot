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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Stream.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Stream.Persistence;

/// <summary>
/// A person has ONE custom line per channel: the unique index is (BroadcasterId, TargetTwitchUserId) among
/// live rows, and a soft-deleted row frees the slot for a re-add.
/// </summary>
public sealed class ShoutoutOverrideConfigurationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ShoutoutOverrideTestDbContext _db;

    public ShoutoutOverrideConfigurationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new ShoutoutOverrideTestDbContext(
            new DbContextOptionsBuilder<ShoutoutOverrideTestDbContext>()
                .UseSqlite(_connection)
                .Options
        );
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TwoOverrides_ForTheSamePerson_ViolateUniqueness()
    {
        Guid broadcasterId = Guid.NewGuid();
        const string targetTwitchUserId = "773007254";

        _db.ShoutoutOverrides.Add(
            new ShoutoutOverride
            {
                BroadcasterId = broadcasterId,
                TargetTwitchUserId = targetTwitchUserId,
                TargetDisplayName = "Viewer One",
                MessageTemplate = "First",
            }
        );
        await _db.SaveChangesAsync();

        _db.ShoutoutOverrides.Add(
            new ShoutoutOverride
            {
                BroadcasterId = broadcasterId,
                TargetTwitchUserId = targetTwitchUserId,
                TargetDisplayName = "Viewer One",
                MessageTemplate = "Second, same person",
            }
        );

        // A person has one line per channel: a second live row for the same broadcaster and target is rejected.
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    /// <summary>
    /// Owner punch list 2026-09-08 §3: "confirm live whether shoutout/raid overrides are genuinely
    /// per-person... one older code note describes the entity as per-channel, not per-viewer." Checked
    /// against the real key: <see cref="ShoutoutOverride.BroadcasterId"/> IS the leading column of the
    /// unique index and of every read (<c>ModerationController</c> always filters by it), so the SAME
    /// target Twitch user id gets a genuinely INDEPENDENT row — and independent message text — per
    /// broadcaster. The old code note was stale; nothing here needed fixing.
    /// </summary>
    [Fact]
    public async Task TheSameTargetPerson_GetsIndependentOverrides_PerBroadcaster()
    {
        const string sameTargetAcrossChannels = "773007254";
        Guid broadcasterA = Guid.NewGuid();
        Guid broadcasterB = Guid.NewGuid();

        _db.ShoutoutOverrides.Add(
            new ShoutoutOverride
            {
                BroadcasterId = broadcasterA,
                TargetTwitchUserId = sameTargetAcrossChannels,
                TargetDisplayName = "Viewer One",
                MessageTemplate = "Channel A's own line for Viewer One",
            }
        );
        _db.ShoutoutOverrides.Add(
            new ShoutoutOverride
            {
                BroadcasterId = broadcasterB,
                TargetTwitchUserId = sameTargetAcrossChannels,
                TargetDisplayName = "Viewer One",
                MessageTemplate = "Channel B's DIFFERENT line for the same viewer",
            }
        );
        await _db.SaveChangesAsync();

        // Both rows persist independently — the unique index (BroadcasterId, TargetTwitchUserId) does
        // NOT collapse them, proving the row is keyed per (channel, viewer), not per viewer alone.
        List<ShoutoutOverride> both = await _db
            .ShoutoutOverrides.AsNoTracking()
            .Where(o => o.TargetTwitchUserId == sameTargetAcrossChannels)
            .ToListAsync();
        both.Should().HaveCount(2);

        // A read scoped to broadcaster A (the real lookup shape every controller action uses) sees ONLY
        // A's own line — B's edit never leaks across the channel boundary.
        ShoutoutOverride? seenByA = await _db
            .ShoutoutOverrides.AsNoTracking()
            .FirstOrDefaultAsync(o =>
                o.BroadcasterId == broadcasterA && o.TargetTwitchUserId == sameTargetAcrossChannels
            );
        seenByA.Should().NotBeNull();
        seenByA.MessageTemplate.Should().Be("Channel A's own line for Viewer One");
    }
}

/// <summary>Minimal single-entity SQLite context — applies the REAL <see cref="ShoutoutOverrideConfiguration"/>
/// so the test exercises the actual production index, not a hand-rolled stand-in that could drift from it.</summary>
internal sealed class ShoutoutOverrideTestDbContext(
    DbContextOptions<ShoutoutOverrideTestDbContext> options
) : DbContext(options)
{
    public DbSet<ShoutoutOverride> ShoutoutOverrides => Set<ShoutoutOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShoutoutOverrideConfiguration());
    }
}
