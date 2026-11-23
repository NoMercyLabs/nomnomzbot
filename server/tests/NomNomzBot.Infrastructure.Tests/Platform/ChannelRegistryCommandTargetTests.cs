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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// The channel cache hands the chat hot path a direct <see cref="CachedCommand.Target"/> only for a command bound
/// to an action or to another command; a plain template command has no target.
/// </summary>
public sealed class ChannelRegistryCommandTargetTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f7001");
    private static readonly Guid HugId = Guid.Parse("0192a000-0000-7000-8000-0000000f7002");

    private static Command Row(string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = ChannelId,
            Name = name,
            NameNormalized = name,
            IsEnabled = true,
        };

    [Fact]
    public async Task Only_commands_bound_to_a_command_or_an_action_carry_a_target_into_the_cache()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "testchannel",
                NameNormalized = "testchannel",
                TwitchChannelId = "123457",
                CreatedAt = DateTime.UtcNow,
            }
        );

        Command hug = Row("hug");
        hug.Id = HugId;
        hug.TemplateResponse = "hugs {user}";

        Command cuddle = Row("cuddle");
        cuddle.Tier = "pipeline";
        cuddle.TargetKind = BindingTargetKind.Command;
        cuddle.TargetCommandId = HugId;

        Command airhorn = Row("airhorn");
        airhorn.Tier = "pipeline";
        airhorn.TargetKind = BindingTargetKind.Action;
        airhorn.TargetActionType = "play_sound";
        airhorn.TargetActionParamsJson = """{"name":"airhorn"}""";

        db.Commands.AddRange(hug, cuddle, airhorn);
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        ChannelRegistry registry = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );

        ChannelContext ctx = await registry.GetOrCreateAsync(ChannelId, "123457", "testchannel");

        ctx.Commands["cuddle"]
            .Target.Should()
            .BeEquivalentTo(new BindingTarget(BindingTargetKind.Command, HugId.ToString()));

        BindingTarget? action = ctx.Commands["airhorn"].Target;
        action.Should().NotBeNull();
        action.Kind.Should().Be(BindingTargetKind.Action);
        action.ActionType.Should().Be("play_sound");
        action.Params!["name"].GetString().Should().Be("airhorn");

        ctx.Commands["hug"].Target.Should().BeNull();
    }
}
