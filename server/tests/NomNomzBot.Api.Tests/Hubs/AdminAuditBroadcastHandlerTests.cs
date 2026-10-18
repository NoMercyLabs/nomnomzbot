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
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the audit log page learns of a new row live: an evaluated platform permission pushes the row's
/// principal, permission and outcome, while the audit page's own read stays silent (its audit row would
/// otherwise reload the page forever).
/// </summary>
public sealed class AdminAuditBroadcastHandlerTests
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

    private static IamAccessEvaluatedEvent Evaluated(string permission, IamOutcome outcome) =>
        new()
        {
            BroadcasterId = Tenant,
            PrincipalId = Principal,
            Permission = permission,
            TargetBroadcasterId = Tenant,
            BreakGlass = false,
            Outcome = outcome,
        };

    [Theory]
    [InlineData(IamOutcome.Allowed)]
    [InlineData(IamOutcome.Denied)]
    public async Task Evaluated_permission_pushes_the_new_audit_row_to_every_open_desk(
        IamOutcome outcome
    )
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveAuditChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminAuditBroadcastHandler(hub).HandleAsync(Evaluated("tenant:suspend", outcome));

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("PrincipalId")!.GetValue(payload).Should().Be(Principal);
        t.GetProperty("Permission")!.GetValue(payload).Should().Be("tenant:suspend");
        t.GetProperty("Outcome")!.GetValue(payload).Should().Be(outcome.ToString());
        t.GetProperty("TargetBroadcasterId")!.GetValue(payload).Should().Be(Tenant);
    }

    [Fact]
    public async Task The_audit_read_itself_pushes_nothing_so_the_page_cannot_reload_itself_forever()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();

        await new AdminAuditBroadcastHandler(hub).HandleAsync(
            Evaluated("audit:read", IamOutcome.Allowed)
        );

        await all.DidNotReceive().ReceiveAuditChanged(Arg.Any<object>());
    }
}
