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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Content.Platform;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// The interceptor fixes templates as they are SAVED; rows authored before the fix keep rendering a
/// stray <c>$</c> until something rewrites them — which is the owner's actual live symptom. This
/// covers that pass, including the case that must NOT be rewritten.
/// </summary>
public class TemplateSyntaxBackfillSeederTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task A_stored_dollar_placeholder_is_rewritten_for_a_known_variable()
    {
        await using SeedTestDbContext db = NewDatabaseWithChannel();
        db.Commands.Add(NewCommand("lurk", "${user} is now lurking"));
        await db.SaveChangesAsync();

        await NewSeeder(db).SeedAsync();

        Command stored = db.Commands.Single();
        stored
            .TemplateResponse.Should()
            .Be(
                "{user} is now lurking",
                "the stored template still rendered a stray $ until rewritten"
            );
    }

    [Fact]
    public async Task A_stored_literal_dollar_before_a_non_variable_is_left_alone()
    {
        // The guard: a backfill that "fixes" this would silently delete a currency symbol from every
        // economy template in the database, which is worse than the bug it is repairing.
        const string Literal = "You have ${totallyNotAVariable} left";

        await using SeedTestDbContext db = NewDatabaseWithChannel();
        db.Commands.Add(NewCommand("balance", Literal));
        await db.SaveChangesAsync();

        await NewSeeder(db).SeedAsync();

        db.Commands.Single().TemplateResponse.Should().Be(Literal);
    }

    [Fact]
    public async Task A_template_LIST_is_rewritten_entry_by_entry()
    {
        await using SeedTestDbContext db = NewDatabaseWithChannel();
        Command command = NewCommand("greet", null);
        command.TemplateResponses = ["${user} hello", "no placeholder here", "${user} rules"];
        db.Commands.Add(command);
        await db.SaveChangesAsync();

        await NewSeeder(db).SeedAsync();

        // Assert each ENTRY, not the count — a list-level check would sail past the wrong element
        // being rewritten, or every element collapsing onto the same value.
        List<string> stored = db.Commands.Single().TemplateResponses!;
        stored[0].Should().Be("{user} hello");
        stored[1].Should().Be("no placeholder here");
        stored[2].Should().Be("{user} rules");
    }

    [Fact]
    public async Task Running_the_backfill_twice_changes_nothing_the_second_time()
    {
        await using SeedTestDbContext db = NewDatabaseWithChannel();
        db.Commands.Add(NewCommand("lurk", "${user} is now lurking"));
        await db.SaveChangesAsync();

        await NewSeeder(db).SeedAsync();
        string afterFirst = db.Commands.Single().TemplateResponse!;

        await NewSeeder(db).SeedAsync();

        db.Commands.Single()
            .TemplateResponse.Should()
            .Be(afterFirst, "the backfill must be idempotent across boots");
    }

    // The REAL validator, not a stub: the backfill refuses to persist a rewrite that does not
    // validate, and a permissive fake would hide exactly that behaviour.
    private static TemplateSyntaxBackfillSeeder NewSeeder(SeedTestDbContext db) =>
        new(
            db,
            new NomNomzBot.Infrastructure.Platform.Templating.TemplateHelperValidator(),
            NullLogger<TemplateSyntaxBackfillSeeder>.Instance
        );

    /// <summary>Commands and timers are tenant-scoped; without their channel the insert trips the FK.</summary>
    private static SeedTestDbContext NewDatabaseWithChannel()
    {
        SeedTestDbContext db = SeedTestDbContext.New(Guid.NewGuid().ToString());
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "100",
                ExternalChannelId = "100",
                Name = "alpha",
                NameNormalized = "alpha",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static Command NewCommand(string name, string? template) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Tenant,
            Name = name,
            NameNormalized = name,
            TemplateResponse = template,
            TemplateResponses = [],
        };
}
