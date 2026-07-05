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

/// <summary>
/// Tenant resolution after the Guid re-key (schema §1.1). The requested channel id is the tenant
/// <see cref="Guid"/> (string form on the wire); the middleware parses it, authorizes it for
/// authenticated callers, and — with no explicit channel — defaults to the caller's OWN channel via
/// <see cref="IChannelAccessService.ResolveOwnChannelAsync"/> (the IDOR fix: NEVER the user id).
/// </summary>
public class TenantResolutionMiddlewareTests
{
    private static readonly Guid ChannelGuid = Guid.Parse("0192a000-0000-7000-8000-000000000001");
    private static readonly Guid VictimChannel = Guid.Parse("0192a000-0000-7000-8000-000000000002");
    private static readonly Guid OwnChannel = Guid.Parse("0192a000-0000-7000-8000-000000000003");
    private static readonly Guid OwnerUser = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");
    private static readonly Guid AttackerUser = Guid.Parse("0192a000-0000-7000-8000-0000000000bb");

    private static TenantResolutionMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new(next);
    }

    private static IChannelAccessService AccessStub(bool allow = false)
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(allow);
        return access;
    }

    private static void Authenticate(DefaultHttpContext context, Guid userId)
    {
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                "TestAuth"
            )
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
        context.Request.RouteValues["channelId"] = ChannelGuid.ToString();

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant(ChannelGuid);
        await access
            .DidNotReceive()
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task InvokeAsync_AnonymousXChannelIdHeader_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.Headers["X-Channel-Id"] = ChannelGuid.ToString();

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant(ChannelGuid);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousQueryString_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.QueryString = new($"?channelId={ChannelGuid}");

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant(ChannelGuid);
    }

    // ── ULID channel id (the API-boundary wire form) ───────────────────────────────────

    [Fact]
    public async Task InvokeAsync_UlidRouteValue_DecodesToGuidAndCanonicalizesRouteValue()
    {
        // The dashboard reaches channel-scoped routes with the channel id in its ULID wire form. The middleware
        // decodes it to the tenant Guid AND rewrites the route value to the raw-Guid form, so the `string channelId`
        // action parameter and the services that Guid.Parse it never see a ULID.
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = new Ulid(ChannelGuid).ToString();

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.Received(1).SetTenant(ChannelGuid);
        context.Request.RouteValues["channelId"].Should().Be(ChannelGuid.ToString());
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUlidChannel_PassesCanonicalGuidToAccessCheck()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                OwnerUser.ToString(),
                ChannelGuid.ToString(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = new Ulid(ChannelGuid).ToString();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant(ChannelGuid);
        await access
            .Received(1)
            .CanResolveTenantAsync(
                OwnerUser.ToString(),
                ChannelGuid.ToString(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task InvokeAsync_MalformedChannelId_Returns400AndStops()
    {
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = "not-a-guid";

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NoSourceAnonymous_DoesNotSetTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context, tenantService, AccessStub());

        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
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
            .CanResolveTenantAsync(
                AttackerUser.ToString(),
                VictimChannel.ToString(),
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        DefaultHttpContext context = new();
        context.Request.QueryString = new($"?channelId={VictimChannel}");
        Authenticate(context, AttackerUser);

        await middleware.InvokeAsync(context, tenantService, access);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
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
            .CanResolveTenantAsync(
                OwnerUser.ToString(),
                ChannelGuid.ToString(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = ChannelGuid.ToString();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant(ChannelGuid);
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_NoChannelSpecified_DefaultsToOwnChannel_NotUserId()
    {
        // The IDOR fix: with no explicit channel, the tenant is the caller's OWN channel resolved by
        // ResolveOwnChannelAsync — NOT the user id (the old broken behavior).
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(OwnerUser.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);

        DefaultHttpContext context = new();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.Received(1).SetTenant(OwnChannel);
        tenantService.DidNotReceive().SetTenant(OwnerUser); // never the user id
        await access
            .Received(1)
            .ResolveOwnChannelAsync(OwnerUser.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_NoChannel_AndNoOwnedChannel_LeavesTenantUnset()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(OwnerUser.ToString(), Arg.Any<CancellationToken>())
            .Returns(Guid.Empty);

        DefaultHttpContext context = new();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access);

        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }
}
