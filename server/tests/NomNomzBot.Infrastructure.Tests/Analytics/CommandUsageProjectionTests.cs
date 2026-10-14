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
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the per-viewer command usage fold: every command run (custom or built-in, failed or not) becomes one
/// <see cref="CommandUsage"/> row for the caller in the channel, linked to the command row and the viewer
/// profile; a replay does not double a row; a reset clears the channel's rows for the rebuild.
/// </summary>
public sealed class CommandUsageProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
    private static readonly DateTime Now = new(2026, 10, 14, 14, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (CommandUsageProjection Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        IUserService userService = AuthTestBuilder.UserService(db, currentUser, scopeFactory);
        return (new(db, new(db, userService)), db);
    }

    private static EventRecord Executed(
        string name,
        bool succeeded = true,
        Guid? channel = null,
        DateTime? at = null
    ) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            channel ?? Channel,
            _seq,
            "CommandExecutedEvent",
            1,
            "domain",
            JsonConvert.SerializeObject(
                new
                {
                    CommandName = name,
                    UserId = "tw-1",
                    Username = "viewer",
                    UserDisplayName = "Viewer",
                    Succeeded = succeeded,
                }
            ),
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            at ?? Now,
            at ?? Now
        );

    [Fact]
    public async Task A_custom_command_run_writes_one_usage_row_linked_to_the_command_and_profile()
    {
        (CommandUsageProjection sut, AuthDbContext db) = Build();
        Command command = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Name = "hug",
            NameNormalized = "hug",
            Tier = "template",
        };
        db.Commands.Add(command);
        await db.SaveChangesAsync();

        await sut.ApplyAsync(Executed("hug"));

        CommandUsage row = db.CommandUsages.Single();
        row.BroadcasterId.Should().Be(Channel);
        row.CommandId.Should().Be(command.Id);
        row.CommandNameSnapshot.Should().Be("hug");
        row.WasSuccessful.Should().BeTrue();
        row.CreatedAt.Should().Be(Now);
        row.ViewerUserId.Should().Be(db.Users.Single(u => u.TwitchUserId == "tw-1").Id);
        row.ViewerProfileId.Should().Be(db.ViewerProfiles.Single().Id);
        row.ViewerProfileId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_built_in_run_without_a_command_row_still_writes_a_usage_row()
    {
        (CommandUsageProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Executed("uptime"));

        CommandUsage row = db.CommandUsages.Single();
        row.CommandId.Should().BeNull();
        row.CommandNameSnapshot.Should().Be("uptime");
    }

    [Fact]
    public async Task A_failed_run_is_recorded_as_not_successful()
    {
        (CommandUsageProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Executed("uptime", succeeded: false));

        db.CommandUsages.Single().WasSuccessful.Should().BeFalse();
    }

    [Fact]
    public async Task Applying_the_same_event_twice_keeps_one_row()
    {
        (CommandUsageProjection sut, AuthDbContext db) = Build();
        EventRecord run = Executed("hug");

        await sut.ApplyAsync(run);
        await sut.ApplyAsync(run);

        db.CommandUsages.Should().ContainSingle();
    }

    [Fact]
    public async Task Reset_clears_only_the_given_channels_rows()
    {
        (CommandUsageProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(Executed("hug"));
        await sut.ApplyAsync(Executed("hug", channel: OtherChannel));

        await sut.ResetAsync(Channel);

        db.CommandUsages.Single().BroadcasterId.Should().Be(OtherChannel);
    }
}
