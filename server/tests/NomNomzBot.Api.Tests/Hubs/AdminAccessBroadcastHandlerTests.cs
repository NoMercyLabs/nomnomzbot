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
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves an operator's access change reaches every open operator access page with its load-bearing fields.</summary>
public sealed class AdminAccessBroadcastHandlerTests
{
    private static readonly Guid Principal = Guid.Parse("0192a000-0000-7000-8000-00000000ee01");
    private static readonly Guid Assignment = Guid.Parse("0192a000-0000-7000-8000-00000000ee02");

    private static (IHubContext<AdminHub, IAdminClient> Hub, IAdminClient All) HubWithRecorder()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        return (hub, all);
    }

    [Fact]
    public async Task Access_change_pushes_action_principal_and_assignment_to_every_open_page()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveAccessChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminAccessBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Action = "assigned",
                PrincipalId = Principal,
                AssignmentId = Assignment,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Action")!.GetValue(payload).Should().Be("assigned");
        t.GetProperty("PrincipalId")!.GetValue(payload).Should().Be(Principal);
        t.GetProperty("AssignmentId")!.GetValue(payload).Should().Be(Assignment);
    }

    [Fact]
    public async Task Revoke_without_a_known_principal_still_pushes_with_a_null_principal()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveAccessChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminAccessBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Action = "revoked",
                AssignmentId = Assignment,
            }
        );

        Type t = payload!.GetType();
        t.GetProperty("Action")!.GetValue(payload).Should().Be("revoked");
        t.GetProperty("PrincipalId")!.GetValue(payload).Should().BeNull();
        t.GetProperty("AssignmentId")!.GetValue(payload).Should().Be(Assignment);
    }
}
