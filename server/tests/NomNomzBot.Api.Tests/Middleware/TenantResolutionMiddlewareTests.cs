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
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
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

    /// <summary>A tenant DB with no rows — every status lookup misses, so <c>RefuseIfSuspendedAsync</c> no-ops.</summary>
    private static ApiTestDbContext EmptyDb() => ApiTestDbContext.New();

    /// <summary>A tenant DB seeded with one channel at the given status, for the suspension-enforcement tests.</summary>
    private static ApiTestDbContext DbWithChannel(Guid channelId, string status)
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                Name = "test-channel",
                NameNormalized = "test-channel",
                Status = status,
            }
        );
        db.SaveChanges();
        return db;
    }

    private static void Authenticate(DefaultHttpContext context, Guid userId)
    {
        context.User = new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

        tenantService.Received(1).SetTenant(ChannelGuid);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousQueryString_SetsTenant()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.QueryString = new($"?channelId={ChannelGuid}");

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, AccessStub(), EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

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

        await middleware.InvokeAsync(context, tenantService, access, EmptyDb());

        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    // ── S088: a suspended tenant's requests are refused, not silently served ───────────

    [Fact]
    public async Task InvokeAsync_AnonymousExplicitChannel_Suspended_Returns403ProblemAndStops()
    {
        // The public-endpoint anonymous path (e.g. the song-request page) never runs
        // CanResolveTenantAsync, so it must check suspension itself — a banned channel's public surface
        // must not keep serving.
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = ChannelGuid.ToString();
        using ApiTestDbContext db = DbWithChannel(ChannelGuid, AuthEnums.ChannelStatus.Suspended);

        await middleware.InvokeAsync(context, tenantService, AccessStub(), db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().Contain("application/problem+json");
        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_AnonymousExplicitChannel_Active_SetsTenantAndContinues()
    {
        // The mirror case: an active tenant's public surface is unaffected.
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        DefaultHttpContext context = new();
        context.Request.RouteValues["channelId"] = ChannelGuid.ToString();
        using ApiTestDbContext db = DbWithChannel(ChannelGuid, AuthEnums.ChannelStatus.Active);

        await middleware.InvokeAsync(context, tenantService, AccessStub(), db);

        tenantService.Received(1).SetTenant(ChannelGuid);
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedOwnChannel_Suspended_Returns403ProblemAndStops()
    {
        // The default-own-channel path (no explicit channelId) must go dark for the owner too — e.g. their
        // own dashboard must not keep resolving to a suspended channel between reinstate and the periodic
        // BotLifecycleService reconcile.
        bool nextCalled = false;
        TenantResolutionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(OwnerUser.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);
        using ApiTestDbContext db = DbWithChannel(
            OwnChannel,
            AuthEnums.ChannelStatus.PlatformBanned
        );

        DefaultHttpContext context = new();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        tenantService.DidNotReceive().SetTenant(Arg.Any<Guid>());
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedOwnChannel_Active_SetsTenantAndContinues()
    {
        TenantResolutionMiddleware middleware = CreateMiddleware();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(OwnerUser.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);
        using ApiTestDbContext db = DbWithChannel(OwnChannel, AuthEnums.ChannelStatus.Active);

        DefaultHttpContext context = new();
        Authenticate(context, OwnerUser);

        await middleware.InvokeAsync(context, tenantService, access, db);

        tenantService.Received(1).SetTenant(OwnChannel);
    }
}
