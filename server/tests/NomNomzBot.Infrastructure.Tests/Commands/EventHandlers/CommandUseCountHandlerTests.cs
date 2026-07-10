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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Infrastructure.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Commands.EventHandlers;

/// <summary>
/// Proves the use-count fold: a successful <see cref="CommandExecutedEvent"/> increments the matching
/// command row's <c>UseCount</c> and stamps <c>LastUsedAt</c> — the numbers the Commands page and the Home
/// top-commands panel read — while failed runs and builtins without a row change nothing.
/// </summary>
public sealed class CommandUseCountHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 14, 0, 0, TimeSpan.Zero);

    private static (CommandUseCountHandler Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        return (new CommandUseCountHandler(db, clock), db);
    }

    private static Command SeedCommand(AuthDbContext db, long useCount = 0)
    {
        Command command = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Name = "hug",
            NameNormalized = "hug",
            Tier = "template",
            MinPermissionLevel = 0,
            UseCount = useCount,
        };
        db.Commands.Add(command);
        db.SaveChanges();
        return command;
    }

    private static CommandExecutedEvent Executed(string name, bool succeeded) =>
        new()
        {
            BroadcasterId = Channel,
            CommandName = name,
            UserId = "tw-1",
            Username = "viewer",
            UserDisplayName = "Viewer",
            Succeeded = succeeded,
        };

    [Fact]
    public async Task A_successful_run_increments_use_count_and_stamps_last_used()
    {
        (CommandUseCountHandler sut, AuthDbContext db) = Build();
        Command command = SeedCommand(db, useCount: 4);

        await sut.HandleAsync(Executed("hug", succeeded: true));

        Command persisted = db.Commands.Single(c => c.Id == command.Id);
        persisted.UseCount.Should().Be(5);
        persisted.LastUsedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task The_event_name_matches_case_insensitively_via_the_normalized_name()
    {
        (CommandUseCountHandler sut, AuthDbContext db) = Build();
        SeedCommand(db);

        await sut.HandleAsync(Executed("HUG", succeeded: true));

        db.Commands.Single().UseCount.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_run_changes_nothing()
    {
        (CommandUseCountHandler sut, AuthDbContext db) = Build();
        Command command = SeedCommand(db, useCount: 4);

        await sut.HandleAsync(Executed("hug", succeeded: false));

        Command persisted = db.Commands.Single(c => c.Id == command.Id);
        persisted.UseCount.Should().Be(4);
        persisted.LastUsedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_builtin_without_a_command_row_is_a_no_op()
    {
        (CommandUseCountHandler sut, AuthDbContext db) = Build();

        await sut.HandleAsync(Executed("uptime", succeeded: true));

        db.Commands.Should().BeEmpty();
    }
}
