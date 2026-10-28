// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
/// V-B2.10: the command form promises two windows — the global guard and a separate per-chatter window, each
/// applied when above 0. The chat hot path reads both from the command row, so the per-user value the
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
        AddCommand(db, "both", cooldown: 10, userCooldown: 90);
        AddCommand(db, "globalonly", cooldown: 30, userCooldown: 90);
        AddCommand(db, "useronly", cooldown: 0, userCooldown: 20);
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
        ctx.Commands["globalonly"].UserCooldown.Should().Be(90, "each window is applied as stored");
        ctx.Commands["useronly"].GlobalCooldown.Should().Be(0);
        ctx.Commands["useronly"].UserCooldown.Should().Be(20);
    }

    private static void AddCommand(AuthDbContext db, string name, int cooldown, int userCooldown) =>
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
                IsEnabled = true,
            }
        );
}
