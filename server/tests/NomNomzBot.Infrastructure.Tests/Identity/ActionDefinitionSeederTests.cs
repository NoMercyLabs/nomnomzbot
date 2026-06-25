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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the ActionDefinitions seed (roles-permissions §7.1) is complete, correct on the rows that deviate
/// from the "default = floor, Low, grantable" convention, and idempotent — re-running adds no duplicate (so
/// the startup seed and any re-run leave exactly one row per action key, and Gate 2 has its catalogue).
/// </summary>
public sealed class ActionDefinitionSeederTests
{
    private static async Task<int> SeedAsync(AuthDbContext db)
    {
        await new ActionDefinitionSeeder(db).SeedAsync();
        return await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seeds_the_catalogue_with_unique_keys_across_both_planes()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        List<ActionDefinition> all = await db.ActionDefinitions.ToListAsync();

        // A substantial catalogue spanning both planes, every key unique.
        all.Should().HaveCountGreaterThan(150);
        all.Select(a => a.ActionKey).Should().OnlyHaveUniqueItems();
        all.Should().Contain(a => a.Plane == AuthPlane.Management);
        all.Should().Contain(a => a.Plane == AuthPlane.Community);
    }

    [Fact]
    public async Task Reseeding_is_idempotent_and_adds_no_duplicates()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);
        int afterFirst = await db.ActionDefinitions.CountAsync();

        int addedOnReseed = await SeedAsync(db);

        addedOnReseed.Should().Be(0);
        (await db.ActionDefinitions.CountAsync()).Should().Be(afterFirst);
    }

    [Fact]
    public async Task Seeds_a_plain_management_row_at_default_equals_floor()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "economy:config:write"
        );
        row.Plane.Should().Be(AuthPlane.Management);
        row.DefaultLevel.Should().Be(30);
        row.FloorLevel.Should().Be(30);
        row.FloorTier.Should().Be(DangerTier.Low);
        row.IsGrantableViaPermit.Should().BeTrue();
    }

    [Fact]
    public async Task Seeds_critical_rows_as_non_grantable()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "roles:manage"
        );
        row.DefaultLevel.Should().Be(40);
        row.FloorLevel.Should().Be(40);
        row.FloorTier.Should().Be(DangerTier.Critical);
        row.IsGrantableViaPermit.Should().BeFalse();
    }

    [Fact]
    public async Task Seeds_the_permit_issue_row_with_default_above_its_floor()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "permit:issue"
        );
        // The one row where default (Broadcaster 40) is above the floor (Editor 30).
        row.DefaultLevel.Should().Be(40);
        row.FloorLevel.Should().Be(30);
    }

    [Fact]
    public async Task Seeds_community_rows_at_everyone_zero()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "economy:catalog:purchase"
        );
        row.Plane.Should().Be(AuthPlane.Community);
        row.DefaultLevel.Should().Be(0);
        row.FloorLevel.Should().Be(0);
        row.IsGrantableViaPermit.Should().BeTrue();
    }

    [Fact]
    public async Task Seeds_economy_account_read_at_the_moderator_floor_not_everyone()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "economy:account:read"
        );
        // Reading ANOTHER member's wallet floors at Moderator (10), not Everyone (0) — Everyone leaked every
        // viewer's balance. Still community-plane: the participant's OWN read is the self-bound /accounts/me.
        row.Plane.Should().Be(AuthPlane.Community);
        row.FloorLevel.Should().Be(10);
        row.DefaultLevel.Should().Be(10);
    }

    [Fact]
    public async Task Reseeding_resyncs_a_drifted_floor_back_to_the_catalogue()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);
        // Simulate a stale install that seeded the key at Everyone (0) under an older catalogue.
        ActionDefinition stale = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "economy:account:read"
        );
        stale.FloorLevel = 0;
        stale.DefaultLevel = 0;
        await db.SaveChangesAsync();

        await SeedAsync(db); // re-seed must re-sync the row to the catalogue, not skip it

        ActionDefinition corrected = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "economy:account:read"
        );
        corrected
            .FloorLevel.Should()
            .Be(10, "the catalogue is authoritative and corrects a drifted floor on re-seed");
        corrected.DefaultLevel.Should().Be(10);
    }
}
