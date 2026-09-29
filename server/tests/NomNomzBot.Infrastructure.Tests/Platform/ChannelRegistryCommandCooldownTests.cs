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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// V-B2.10: the command form promises two windows — the global guard, and a separate per-chatter window when
/// the per-user switch is on. The chat hot path reads both from the command row, so the per-user value the
/// streamer saved is the one a chatter is held to, and the global guard still applies beside it.
/// </summary>
public sealed class ChannelRegistryCommandCooldownTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");

    [Fact]
    public async Task A_command_runs_its_global_guard_and_its_separate_per_user_window()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "testchannel",
                NameNormalized = "testchannel",
                TwitchChannelId = "123456",
                CreatedAt = DateTime.UtcNow,
            }
        );
        AddCommand(db, "both", cooldown: 10, userCooldown: 90, perUser: true);
        AddCommand(db, "globalonly", cooldown: 30, userCooldown: 90, perUser: false);
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ChannelRegistry registry = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );

        ChannelContext ctx = await registry.GetOrCreateAsync(ChannelId, "123456", "testchannel");

        ctx.Commands["both"].GlobalCooldown.Should().Be(10);
        ctx.Commands["both"].UserCooldown.Should().Be(90, "the saved per-user window is enforced");
        ctx.Commands["globalonly"].GlobalCooldown.Should().Be(30);
        ctx.Commands["globalonly"]
            .UserCooldown.Should()
            .Be(0, "the per-user switch is off, so its value is not applied");
    }

    private static void AddCommand(
        AuthDbContext db,
        string name,
        int cooldown,
        int userCooldown,
        bool perUser
    ) =>
        db.Commands.Add(
            new()
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
            }
        );
}
