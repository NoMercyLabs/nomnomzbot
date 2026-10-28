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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A channel's permission changes reach its open dashboards live: the team roles and the action-level matrix
/// reload on the Permissions page. A management-role change and an action-level override are forwarded as a
/// <c>permissions</c> config change, so the page needs no bespoke hub event.
/// </summary>
public sealed class ChannelPermissionsBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<ManagementRoleChangedEvent>,
        IEventHandler<ActionLevelOverriddenEvent>
{
    private const string Domain = "permissions";

    public Task HandleAsync(ManagementRoleChangedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, @event.TargetUserId, RoleAction(@event), ct);

    public Task HandleAsync(ActionLevelOverriddenEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, @event.ActionDefinitionId, "updated", ct);

    private static string RoleAction(ManagementRoleChangedEvent @event)
    {
        if (@event.NewRole is null)
            return "deleted";

        return @event.OldRole is null ? "created" : "updated";
    }

    private Task PushAsync(DomainEventBase @event, Guid entity, string action, CancellationToken ct)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = @event.BroadcasterId.ToString();

        return notifier.SendConfigChangedAsync(
            channel,
            new ConfigChangedDto(channel, Domain, entity.ToString(), action),
            ct
        );
    }
}
