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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// The channel cache hands the chat hot path a direct <see cref="CachedChatTrigger.Target"/> only for a command
/// or action binding; a pipeline-bound or plain-response trigger keeps the graph path and has no target.
/// </summary>
public sealed class ChannelRegistryChatTriggerTargetTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-0000000f6002");
    private static readonly Guid CommandTriggerId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f6003"
    );
    private static readonly Guid ActionTriggerId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f6004"
    );
    private static readonly Guid PipelineTriggerId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f6005"
    );
    private static readonly Guid ResponseTriggerId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f6006"
    );

    private static ChatTrigger Row(Guid id, string pattern) =>
        new()
        {
            Id = id,
            BroadcasterId = ChannelId,
            Pattern = pattern,
            IsEnabled = true,
        };

    [Fact]
    public async Task Only_command_and_action_triggers_carry_a_target_into_the_cache()
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

        ChatTrigger commandTrigger = Row(CommandTriggerId, "thirsty");
        commandTrigger.TargetKind = BindingTargetKind.Command;
        commandTrigger.TargetCommandId = CommandId;

        ChatTrigger actionTrigger = Row(ActionTriggerId, "airhorn");
        actionTrigger.TargetKind = BindingTargetKind.Action;
        actionTrigger.TargetActionType = "play_sound";
        actionTrigger.TargetActionParamsJson = """{"clip":"airhorn"}""";

        ChatTrigger pipelineTrigger = Row(PipelineTriggerId, "chain");
        ChatTrigger responseTrigger = Row(ResponseTriggerId, "hello");
        responseTrigger.Response = "hi {user}";

        db.ChatTriggers.AddRange(commandTrigger, actionTrigger, pipelineTrigger, responseTrigger);
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        ChannelRegistry registry = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );

        ChannelContext ctx = await registry.GetOrCreateAsync(ChannelId, "123456", "testchannel");

        ctx.ChatTriggers[CommandTriggerId]
            .Target.Should()
            .BeEquivalentTo(
                new Domain.Commands.ValueObjects.BindingTarget(
                    BindingTargetKind.Command,
                    CommandId.ToString()
                )
            );

        Domain.Commands.ValueObjects.BindingTarget? action = ctx.ChatTriggers[
            ActionTriggerId
        ].Target;
        action.Should().NotBeNull();
        action!.Kind.Should().Be(BindingTargetKind.Action);
        action.ActionType.Should().Be("play_sound");
        action.Params.Should().ContainKey("clip");
        action.Params!["clip"].GetString().Should().Be("airhorn");

        ctx.ChatTriggers[PipelineTriggerId].Target.Should().BeNull();
        ctx.ChatTriggers[ResponseTriggerId].Target.Should().BeNull();
    }
}
