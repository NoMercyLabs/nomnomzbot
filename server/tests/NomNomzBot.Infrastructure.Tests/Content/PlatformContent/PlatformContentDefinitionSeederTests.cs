// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// S-ADMIN-2a §7 exit condition: the migration adds nullable provenance columns (schema-safe on a
/// populated database — no NOT-NULL default to orphan existing rows), and THIS seeder is what actually
/// stamps existing tenant <see cref="ChannelBuiltinCommand"/> rows with correct provenance afterward — the
/// backfill pass that makes pre-existing tenants reachable by a future <c>update_in_place_where_untouched</c>
/// publish instead of silently excluded.
/// </summary>
public sealed class PlatformContentDefinitionSeederTests : IAsyncDisposable
{
    private readonly PlatformContentTestDbContext _db = PlatformContentTestDbContext.New();

    private async Task<Channel> AddChannelAsync(string name)
    {
        Channel channel = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
        };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        return channel;
    }

    [Fact]
    public async Task SeedAsync_BackfillsPreExistingTenantRow_WithCorrectProvenance_NotAnOrphaningDefault()
    {
        Channel channel = await AddChannelAsync("pre-existing-tenant");
        // Simulates a row written by DefaultCommandsSeeder BEFORE this slice existed — a raw migration
        // upgrade leaves such a row's new provenance columns NULL; this seeder must be what fixes that.
        ChannelBuiltinCommand preExisting = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel.Id,
            BuiltinKey = "sr",
            IsEnabled = true,
            OverridesJson = null,
        };
        _db.ChannelBuiltinCommands.Add(preExisting);
        await _db.SaveChangesAsync();

        PlatformContentDefinitionSeeder seeder = new(_db);
        await seeder.SeedAsync();

        PlatformContentDefinition definition = await _db.PlatformContentDefinitions.AsNoTracking()
            .SingleAsync(d => d.Kind == PlatformContentKinds.Command && d.Key == "sr");
        PlatformContentVersion v1 = await _db.PlatformContentVersions.AsNoTracking()
            .SingleAsync(v => v.DefinitionId == definition.Id && v.Version == 1);

        ChannelBuiltinCommand after = await _db.ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == preExisting.Id);

        Assert.Equal(definition.Id, after.PlatformSourceDefinitionId);
        Assert.Equal(1, after.PlatformSourceVersion);
        Assert.Equal(v1.ContentHash, after.PlatformSourceHash);
        Assert.NotNull(after.PlatformSourceSyncedAt);
    }

    [Fact]
    public async Task SeedAsync_CreatesADefinitionAndV1VersionForEveryDefaultKey()
    {
        PlatformContentDefinitionSeeder seeder = new(_db);
        await seeder.SeedAsync();

        List<PlatformContentDefinition> definitions = await _db
            .PlatformContentDefinitions.AsNoTracking()
            .Where(d => d.Kind == PlatformContentKinds.Command)
            .ToListAsync();

        Assert.Equal(
            new[] { "sr", "skip", "queue", "volume", "song" }.OrderBy(k => k),
            definitions.Select(d => d.Key).OrderBy(k => k)
        );
        Assert.All(definitions, d => Assert.NotNull(d.CurrentVersionId));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_AndNeverOverwritesAnAlreadyStampedRow()
    {
        PlatformContentDefinitionSeeder seeder = new(_db);
        await seeder.SeedAsync();

        Channel channel = await AddChannelAsync("already-stamped-tenant");
        PlatformContentDefinition srDefinition = await _db.PlatformContentDefinitions.AsNoTracking()
            .SingleAsync(d => d.Kind == PlatformContentKinds.Command && d.Key == "sr");

        // A row the PUBLISH engine already updated to v2 — provenance must survive a second seeder run.
        ChannelBuiltinCommand alreadyOnV2 = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel.Id,
            BuiltinKey = "sr",
            IsEnabled = true,
            OverridesJson = "{\"cooldownSeconds\":5}",
            PlatformSourceDefinitionId = srDefinition.Id,
            PlatformSourceVersion = 2,
            PlatformSourceHash = "some-v2-hash",
            PlatformSourceSyncedAt = DateTime.UtcNow,
        };
        _db.ChannelBuiltinCommands.Add(alreadyOnV2);
        await _db.SaveChangesAsync();

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        int definitionCount = await _db.PlatformContentDefinitions.CountAsync(d =>
            d.Kind == PlatformContentKinds.Command
        );
        Assert.Equal(5, definitionCount); // no duplicates created on re-run

        ChannelBuiltinCommand after = await _db.ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == alreadyOnV2.Id);
        Assert.Equal(2, after.PlatformSourceVersion); // untouched, not reset back to v1
        Assert.Equal("some-v2-hash", after.PlatformSourceHash);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
