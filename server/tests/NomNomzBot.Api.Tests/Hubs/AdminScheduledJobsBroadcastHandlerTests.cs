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
using NomNomzBot.Domain.Commands.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a retried scheduled job reaches every open operator scheduled jobs page with the change and both task ids.</summary>
public sealed class AdminScheduledJobsBroadcastHandlerTests
{
    private static readonly Guid Original = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid Retried = Guid.Parse("0192a000-0000-7000-8000-00000000f002");

    [Fact]
    public async Task Retried_job_pushes_the_change_and_both_task_ids()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        object? payload = null;
        all.ReceiveScheduledJobsChanged(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminScheduledJobsBroadcastHandler(hub).HandleAsync(
            new ScheduledJobsChangedEvent
            {
                BroadcasterId = Guid.NewGuid(),
                Change = "retried",
                OriginalTaskId = Original,
                NewTaskId = Retried,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Change")!.GetValue(payload).Should().Be("retried");
        t.GetProperty("OriginalTaskId")!.GetValue(payload).Should().Be(Original);
        t.GetProperty("NewTaskId")!.GetValue(payload).Should().Be(Retried);
    }
}
