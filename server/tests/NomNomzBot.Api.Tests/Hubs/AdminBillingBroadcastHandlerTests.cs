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
using NomNomzBot.Domain.Billing.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a billing change reaches every open operator desk with the fields the desk reloads by.</summary>
public sealed class AdminBillingBroadcastHandlerTests
{
    [Fact]
    public async Task Billing_change_pushes_area_action_subject_and_target_channel_to_every_desk()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        object? payload = null;
        all.ReceiveBillingChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);
        Guid subject = Guid.NewGuid();
        Guid channel = Guid.NewGuid();

        await new AdminBillingBroadcastHandler(hub).HandleAsync(
            new BillingChangedEvent
            {
                BroadcasterId = Guid.Empty,
                Area = "invoice",
                Action = "refunded",
                SubjectId = subject,
                TargetBroadcasterId = channel,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Area")!.GetValue(payload).Should().Be("invoice");
        t.GetProperty("Action")!.GetValue(payload).Should().Be("refunded");
        t.GetProperty("SubjectId")!.GetValue(payload).Should().Be(subject);
        t.GetProperty("TargetBroadcasterId")!.GetValue(payload).Should().Be(channel);
    }
}
