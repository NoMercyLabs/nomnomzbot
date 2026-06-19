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
using FluentAssertions;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Middleware;

public class TenantResolutionMiddlewareTests
{
    private static TenantResolutionMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new(next);
    }

    private static IChannelAccessService AccessStub(bool allow = false)
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(allow);
        return access;
    }

    private static void Authenticate(DefaultHttpContext context, string userId)
    {
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth")
        );
    }

    // ── Anonymous requests: the channel id is a public-endpoint selector ───────────────

    [Fact]
    public async Task InvokeAsync_AnonymousRouteValue_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = AccessStub();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = "chan-route-123";

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant("chan-route-123");
        await access.DidNotReceive().CanResolveTenantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AnonymousXChannelIdHeader_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.Headers["X-Channel-Id"] = "chan-header-456";

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant("chan-header-456");
    }

    [Fact]
    public async Task InvokeAsync_AnonymousQueryString_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.QueryString = new("?channelId=chan-query-789");

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant("chan-query-789");
    }

    [Fact]
    public async Task InvokeAsync_RouteValueTakesPrecedenceOverHeader()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = "from-route";
        context.Request.Headers["X-Channel-Id"] = "from-header";

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant("from-route");
        tenantService.DidNotReceive().SetTenant("from-header");
    }

    [Fact]
    public async Task InvokeAsync_HeaderTakesPrecedenceOverQuery()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.Headers["X-Channel-Id"] = "from-header";
        context.Request.QueryString = new("?channelId=from-query");

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant("from-header");
        tenantService.DidNotReceive().SetTenant("from-query");
    }

    [Fact]
    public async Task InvokeAsync_NoSourceAnonymous_DoesNotSetTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.DidNotReceive().SetTenant(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_EmptyRouteValueAnonymous_DoesNotSetTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = ""; // empty string

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.DidNotReceive().SetTenant(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext_WhenNoShortCircuit()
    {
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        nextCalled.Should().BeTrue();
    }

    // ── Authenticated requests: the channel id must be authorized (the IDOR fix) ────────

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_UnauthorizedChannel_Returns403AndStops()
    {
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync("attacker", "victim-channel", Arg.Any<CancellationToken>())
            .Returns(false);

        DefaultHttpContext context = new();
        context.Request.QueryString = new("?channelId=victim-channel");
        Authenticate(context, "attacker");

        await middleware.InvokeAsync(context, tenantService, access);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        tenantService.DidNotReceive().SetTenant(Arg.Any<string>());
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_AuthorizedChannel_SetsTenantAndContinues()
    {
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync("mod-user", "moderated-channel", Arg.Any<CancellationToken>())
            .Returns(true);

        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = "moderated-channel";
        Authenticate(context, "mod-user");

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant("moderated-channel");
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_NoChannelSpecified_DefaultsToOwnChannel()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();

        DefaultHttpContext context = new();
        Authenticate(context, "owner-123");

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant("owner-123");
        await access.DidNotReceive().CanResolveTenantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_OwnChannelInRoute_IsAuthorized()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync("owner-123", "owner-123", Arg.Any<CancellationToken>())
            .Returns(true);

        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = "owner-123";
        Authenticate(context, "owner-123");

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant("owner-123");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
