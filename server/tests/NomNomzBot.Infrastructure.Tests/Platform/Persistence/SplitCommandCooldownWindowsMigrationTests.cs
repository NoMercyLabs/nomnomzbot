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
/// V-B2.10's data step, run on SQLite (the SelfHostLite runtime): a per-user command saved in the old shape
/// (its per-chatter window in CooldownSeconds, UserCooldownSeconds empty) keeps the same per-chatter window
/// after the upgrade, now held in UserCooldownSeconds, with no global guard it never had. Commands already in
/// the new shape, and global-only commands, are left exactly as they were.
/// </summary>
public sealed class SplitCommandCooldownWindowsMigrationTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f7001");

    [Fact]
    public async Task An_old_shape_per_user_command_keeps_its_window_and_other_commands_are_untouched()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        Guid oldShape = AddCommand(db, "oldshape", cooldown: 20, userCooldown: 0, perUser: true);
        Guid newShape = AddCommand(db, "newshape", cooldown: 10, userCooldown: 90, perUser: true);
        Guid globalOnly = AddCommand(db, "global", cooldown: 30, userCooldown: 0, perUser: false);
        await db.SaveChangesAsync();

        foreach (
            SqlOperation operation in new SplitCommandCooldownWindows().UpOperations.OfType<SqlOperation>()
        )
            await db.Database.ExecuteSqlRawAsync(operation.Sql);

        List<Command> rows = await db.Commands.AsNoTracking().ToListAsync();
        Command migrated = rows.Single(c => c.Id == oldShape);
        migrated.CooldownSeconds.Should().Be(0);
        migrated.UserCooldownSeconds.Should().Be(20);
        migrated.CooldownPerUser.Should().BeTrue();
        Command current = rows.Single(c => c.Id == newShape);
        current.CooldownSeconds.Should().Be(10);
        current.UserCooldownSeconds.Should().Be(90);
        Command global = rows.Single(c => c.Id == globalOnly);
        global.CooldownSeconds.Should().Be(30);
        global.UserCooldownSeconds.Should().Be(0);
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
            CooldownPerUser = perUser,
            IsEnabled = true,
        };
        db.Commands.Add(command);
        return command.Id;
    }
}
