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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a channel's role and action-level changes reach its dashboards as a <c>permissions</c> config change.</summary>
public sealed class ChannelPermissionsBroadcastHandlerTests
{
    [Fact]
    public async Task A_role_change_pushes_an_updated_permissions_change_for_the_member()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        Guid member = Guid.CreateVersion7();

        await new ChannelPermissionsBroadcastHandler(notifier).HandleAsync(
            new ManagementRoleChangedEvent
            {
                BroadcasterId = channel,
                TargetUserId = member,
                OldRole = ManagementRole.Moderator,
                NewRole = ManagementRole.Editor,
                Source = MembershipSource.BotGrant,
                ChangedByUserId = channel,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Domain == "permissions"
                    && dto.EntityId == member.ToString()
                    && dto.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_removed_role_pushes_a_deleted_permissions_change()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new ChannelPermissionsBroadcastHandler(notifier).HandleAsync(
            new ManagementRoleChangedEvent
            {
                BroadcasterId = channel,
                TargetUserId = Guid.CreateVersion7(),
                OldRole = ManagementRole.Moderator,
                NewRole = null,
                Source = MembershipSource.BotGrant,
                ChangedByUserId = channel,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.Domain == "permissions" && dto.Action == "deleted"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_new_role_pushes_a_created_permissions_change()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new ChannelPermissionsBroadcastHandler(notifier).HandleAsync(
            new ManagementRoleChangedEvent
            {
                BroadcasterId = channel,
                TargetUserId = Guid.CreateVersion7(),
                OldRole = null,
                NewRole = ManagementRole.Moderator,
                Source = MembershipSource.BotGrant,
                ChangedByUserId = channel,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.Domain == "permissions" && dto.Action == "created"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_action_level_override_pushes_a_permissions_change_for_the_action()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        Guid action = Guid.CreateVersion7();

        await new ChannelPermissionsBroadcastHandler(notifier).HandleAsync(
            new ActionLevelOverriddenEvent
            {
                BroadcasterId = channel,
                ActionDefinitionId = action,
                ActionKey = "commands.manage",
                OldLevel = null,
                NewEffectiveLevel = 20,
                SetByUserId = channel,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Domain == "permissions"
                    && dto.EntityId == action.ToString()
                    && dto.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_event_without_a_channel_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new ChannelPermissionsBroadcastHandler(notifier).HandleAsync(
            new ActionLevelOverriddenEvent
            {
                BroadcasterId = Guid.Empty,
                ActionDefinitionId = Guid.CreateVersion7(),
                ActionKey = "commands.manage",
                OldLevel = null,
                NewEffectiveLevel = 20,
                SetByUserId = Guid.CreateVersion7(),
            }
        );

        await notifier.DidNotReceiveWithAnyArgs().SendConfigChangedAsync(default!, default!);
    }

    [Theory]
    [InlineData(typeof(ManagementRoleChangedEvent))]
    [InlineData(typeof(ActionLevelOverriddenEvent))]
    public void Handler_satisfies_the_assembly_scan_predicate(Type eventType)
    {
        Type handlerType = typeof(ChannelPermissionsBroadcastHandler);

        handlerType.IsPublic.Should().BeTrue();
        handlerType.IsClass.Should().BeTrue();
        handlerType.IsAbstract.Should().BeFalse();
        handlerType
            .GetInterfaces()
            .Should()
            .Contain(typeof(IEventHandler<>).MakeGenericType(eventType));
    }
}
