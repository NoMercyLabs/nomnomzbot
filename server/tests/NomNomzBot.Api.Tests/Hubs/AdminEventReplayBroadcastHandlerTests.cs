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
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.EventStore.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a finished event replay reaches every open operator event replay page with its tenant, projection, status and applied count.</summary>
public sealed class AdminEventReplayBroadcastHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-00000000ee01");

    [Fact]
    public async Task Completed_replay_pushes_tenant_projection_status_and_applied_count()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        object? payload = null;
        all.ReceiveEventReplayChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminEventReplayBroadcastHandler(hub).HandleAsync(
            new ReplayStatusChangedEvent
            {
                BroadcasterId = Tenant,
                ReplayId = Guid.NewGuid(),
                ProjectionName = "chat-history",
                Status = "completed",
                FromPosition = 10,
                ToPosition = 52,
                ProcessedCount = 42,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("BroadcasterId")!.GetValue(payload).Should().Be(Tenant);
        t.GetProperty("ProjectionName")!.GetValue(payload).Should().Be("chat-history");
        t.GetProperty("Status")!.GetValue(payload).Should().Be("completed");
        t.GetProperty("ProcessedCount")!.GetValue(payload).Should().Be(42L);
    }
}
