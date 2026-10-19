// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the operator hub's system status reaches only the right admins: the platform stats go to connections
/// holding <c>platform:analytics:read</c>, every other <c>iam:manage</c> admin gets the health snapshot with no stats.
/// </summary>
public sealed class AdminHubStatusGatingTests
{
    [Theory]
    [InlineData(true, AdminHub.AnalyticsGroup)]
    [InlineData(false, AdminHub.NoAnalyticsGroup)]
    public async Task A_connection_joins_the_group_that_matches_its_analytics_permission(
        bool allowed,
        string expectedGroup
    )
    {
        IAuthorizationService authorization = Substitute.For<IAuthorizationService>();
        authorization
            .AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<object?>(),
                IamPermissionKeys.PlatformAnalyticsRead
            )
            .Returns(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity("test")));
        IGroupManager groups = Substitute.For<IGroupManager>();
        AdminHub hub = new(authorization) { Context = context, Groups = groups };

        await hub.OnConnectedAsync();

        await groups
            .Received(1)
            .AddToGroupAsync("conn-1", expectedGroup, Arg.Any<CancellationToken>());
        await groups
            .Received(1)
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_heartbeat_sends_stats_to_the_analytics_group_and_health_only_to_the_rest()
    {
        IAdminClient analytics = Substitute.For<IAdminClient>();
        IAdminClient others = Substitute.For<IAdminClient>();
        IHubClients<IAdminClient> clients = Substitute.For<IHubClients<IAdminClient>>();
        clients.Group(AdminHub.AnalyticsGroup).Returns(analytics);
        clients.Group(AdminHub.NoAnalyticsGroup).Returns(others);
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        hub.Clients.Returns(clients);

        AdminSystemDto system = new("healthy", [], "1.0.0", 512, 3.5);
        AdminStatsDto stats = new(4, 2, 100, "healthy", 3600, 250);
        IAdminService admin = Substitute.For<IAdminService>();
        admin.GetSystemHealthAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(system));
        admin.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(stats));
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(admin)
            .BuildServiceProvider();
        IServiceScopeFactory scopes = services.GetRequiredService<IServiceScopeFactory>();

        AdminHubStatusPublisher publisher = new(
            hub,
            scopes,
            NullLogger<AdminHubStatusPublisher>.Instance
        );

        await publisher.PublishOnceAsync(CancellationToken.None);

        await analytics.Received(1).ReceiveSystemStatus(Arg.Is<object>(o => Has(o, system, stats)));
        await others.Received(1).ReceiveSystemStatus(Arg.Is<object>(o => Has(o, system, null)));
    }

    private static bool Has(object payload, AdminSystemDto system, AdminStatsDto? stats)
    {
        object? sentSystem = payload.GetType().GetProperty("System")?.GetValue(payload);
        object? sentStats = payload.GetType().GetProperty("Stats")?.GetValue(payload);
        return ReferenceEquals(sentSystem, system) && ReferenceEquals(sentStats, stats);
    }
}
