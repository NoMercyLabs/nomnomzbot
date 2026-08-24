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
using NomNomzBot.Api.Middleware;
using NSubstitute;
// IHostEnvironment is in Microsoft.Extensions.Hosting.Abstractions
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostEnvironment;

namespace NomNomzBot.Api.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static SecurityHeadersMiddleware CreateMiddleware(
        RequestDelegate next,
        bool isDevelopment
    )
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return new(next, environment);
    }

    private static DefaultHttpContext CreateContext(string path, bool isHttps = true)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Scheme = isHttps ? "https" : "http";
        context.Response.Body = new MemoryStream();
        return context;
    }

    // The middleware decides both headers from the request path before delegating downstream, so a
    // no-op "next" is enough to exercise it — no real static-file/SPA pipeline is needed.
    private static Task NoOpNext(HttpContext _) => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_DashboardPath_SetsContentSecurityPolicy()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/");

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("Content-Security-Policy");
        context
            .Response.Headers["Content-Security-Policy"]
            .ToString()
            .Should()
            .Contain("wasm-unsafe-eval");
    }

    [Fact]
    public async Task InvokeAsync_JsonApiPath_DoesNotSetContentSecurityPolicy()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/api/v1/channels");

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task InvokeAsync_OverlayPath_DoesNotSetDashboardCsp()
    {
        // The overlay host sets its own strict per-widget nonce CSP via a <meta> tag — the global
        // dashboard policy must not be layered on top of it (it doesn't know the overlay's nonce).
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/overlay/widget/abc123");

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task InvokeAsync_ProductionHttps_SetsHsts()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/", isHttps: true);

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("Strict-Transport-Security");
        context
            .Response.Headers["Strict-Transport-Security"]
            .ToString()
            .Should()
            .Contain("max-age=31536000");
    }

    [Fact]
    public async Task InvokeAsync_Development_DoesNotSetHsts()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: true);
        DefaultHttpContext context = CreateContext("/", isHttps: true);

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task InvokeAsync_ProductionHttp_DoesNotSetHsts()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/", isHttps: false);

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }
}
