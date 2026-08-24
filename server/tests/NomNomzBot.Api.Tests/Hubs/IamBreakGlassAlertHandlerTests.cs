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
using NomNomzBot.Domain.Identity.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the break-glass watch (S086f — <c>IamAccessEvaluatedEvent</c> had zero consumers): a denied
/// platform-permission attempt and an allowed break-glass evaluation both land on the operator log feed
/// naming the principal and the permission key; a routine allowed evaluation stays silent.
/// </summary>
public sealed class IamBreakGlassAlertHandlerTests
{
    private static readonly Guid Principal = Guid.Parse("0192f200-0000-7000-8000-00000000a001");
    private static readonly Guid Tenant = Guid.Parse("0192f200-0000-7000-8000-00000000b001");

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
    public async Task Denied_evaluation_logs_the_principal_and_the_permission_key()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? log = null;
        all.ReceiveLog(Arg.Do<object>(p => log = p)).Returns(Task.CompletedTask);

        await new IamBreakGlassAlertHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Tenant,
                PrincipalId = Principal,
                Permission = "iam:principal:create",
                TargetBroadcasterId = Tenant,
                BreakGlass = false,
                Outcome = IamOutcome.Denied,
            }
        );

        log.Should().NotBeNull();
        string message = log!.GetType().GetProperty("Message")!.GetValue(log)!.ToString()!;
        message.Should().Contain(Principal.ToString());
        message.Should().Contain("iam:principal:create");
        log.GetType().GetProperty("Type")!.GetValue(log).Should().Be("warning");
    }

    [Fact]
    public async Task Allowed_break_glass_evaluation_is_also_logged()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? log = null;
        all.ReceiveLog(Arg.Do<object>(p => log = p)).Returns(Task.CompletedTask);

        await new IamBreakGlassAlertHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Tenant,
                PrincipalId = Principal,
                Permission = "tenant:access",
                TargetBroadcasterId = Tenant,
                BreakGlass = true,
                Outcome = IamOutcome.Allowed,
            }
        );

        log.Should().NotBeNull();
    }

    [Fact]
    public async Task Routine_allowed_evaluation_stays_silent()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();

        await new IamBreakGlassAlertHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Tenant,
                PrincipalId = Principal,
                Permission = "tenant:access",
                TargetBroadcasterId = Tenant,
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
            }
        );

        await all.DidNotReceive().ReceiveLog(Arg.Any<object>());
    }
}
