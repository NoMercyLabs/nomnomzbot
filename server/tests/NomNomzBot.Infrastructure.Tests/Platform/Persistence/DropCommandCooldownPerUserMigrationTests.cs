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
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Migrations.Sqlite.Migrations;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The data step of dropping the redundant per-user switch: a command's two windows (global, per user) are
/// each live when above 0. A row that had the switch on keeps its per-chatter window and loses a global guard
/// it never had; a row that had the switch off keeps its global window and its stale per-user number is
/// zeroed so it does not suddenly start applying.
/// </summary>
public sealed class DropCommandCooldownPerUserMigrationTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f7002");

    [Fact]
    public async Task Each_old_shape_keeps_the_behavior_it_had_under_two_independent_windows()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        Guid perUserOld = AddCommand(
            db,
            "peruserold",
            cooldown: 20,
            userCooldown: 0,
            perUser: true
        );
        Guid perUserBoth = AddCommand(
            db,
            "peruserboth",
            cooldown: 10,
            userCooldown: 90,
            perUser: true
        );
        Guid globalStale = AddCommand(
            db,
            "globalstale",
            cooldown: 30,
            userCooldown: 45,
            perUser: false
        );
        Guid none = AddCommand(db, "none", cooldown: 0, userCooldown: 0, perUser: false);
        await db.SaveChangesAsync();

        // The column is gone from the model; recreate it as the old schema had it, then flag the old per-user rows.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Commands\" ADD COLUMN \"CooldownPerUser\" INTEGER NOT NULL DEFAULT 0"
        );
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Commands\" SET \"CooldownPerUser\" = 1 WHERE \"NameNormalized\" IN ('peruserold', 'peruserboth')"
        );

        foreach (
            SqlOperation operation in new DropCommandCooldownPerUser().UpOperations.OfType<SqlOperation>()
        )
            await db.Database.ExecuteSqlRawAsync(operation.Sql);

        List<Command> rows = await db.Commands.AsNoTracking().ToListAsync();
        Command a = rows.Single(c => c.Id == perUserOld);
        (a.CooldownSeconds, a.UserCooldownSeconds).Should().Be((0, 20));
        Command b = rows.Single(c => c.Id == perUserBoth);
        (b.CooldownSeconds, b.UserCooldownSeconds).Should().Be((10, 90));
        Command c2 = rows.Single(c => c.Id == globalStale);
        (c2.CooldownSeconds, c2.UserCooldownSeconds).Should().Be((30, 0));
        Command d = rows.Single(c => c.Id == none);
        (d.CooldownSeconds, d.UserCooldownSeconds).Should().Be((0, 0));
    }

    [Fact]
    public void The_migration_drops_the_column()
    {
        new DropCommandCooldownPerUser()
            .UpOperations.OfType<DropColumnOperation>()
            .Should()
            .ContainSingle(o => o.Table == "Commands" && o.Name == "CooldownPerUser");
    }

    private static Guid AddCommand(
        AuthDbContext db,
        string name,
        int cooldown,
        int userCooldown,
        bool perUser
    )
    {
        Command command = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = ChannelId,
            Name = name,
            NameNormalized = name,
            Tier = "template",
            TemplateResponse = "hi",
            CooldownSeconds = cooldown,
            UserCooldownSeconds = userCooldown,
            IsEnabled = true,
        };
        db.Commands.Add(command);
        return command.Id;
    }
}
