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
using Microsoft.Extensions.Configuration;
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
        IConfiguration configuration = new ConfigurationBuilder().Build();
        return new(next, environment, configuration);
    }

    private static DefaultHttpContext CreateContext(string path, bool isHttps = true)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Scheme = isHttps ? "https" : "http";
        // A host is required: the public-origin resolution falls back to the loopback default when the
        // request carries none, and HSTS is correctly withheld from a loopback origin.
        context.Request.Host = new("dash.example.test");
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
    public async Task InvokeAsync_DashboardPath_AllowsTheCodeEditorCdnStylesheet()
    {
        // Monaco resolves `vs/editor/editor.main` only once its own AMD css plugin has fetched
        // editor.main.css from the CDN. Trusting that CDN for scripts alone is NOT enough: with it absent
        // from style-src the browser refuses the stylesheet, the module graph never completes, the
        // `monaco` global never appears, and the editor silently degrades to a plain textarea.
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/");

        await middleware.InvokeAsync(context);

        string policy = context.Response.Headers["Content-Security-Policy"].ToString();
        string styleSrc = policy
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Single(directive => directive.StartsWith("style-src ", StringComparison.Ordinal));

        styleSrc.Should().Contain("https://cdn.jsdelivr.net");
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
    public async Task InvokeAsync_EditorPath_AllowsInlineScriptForTheGeneratedPreviewDocument()
    {
        // The editor's live preview hands a client-built document to a `srcdoc` iframe, and a srcdoc
        // document inherits the creator's policy — so the preview's import map, SDK stub and esbuild bundle
        // are all judged against THIS header. Under the dashboard policy (no 'unsafe-inline') all three are
        // blocked and the preview pane renders an empty frame. Neither a nonce nor a hash can stand in: the
        // page is a static file with no per-response nonce, and the bundle changes on every keystroke.
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/editor/index.html");

        await middleware.InvokeAsync(context);

        string scriptSrc = DirectiveOf(context, "script-src");
        scriptSrc.Should().Contain("'unsafe-inline'");
        // esbuild-wasm still needs both eval forms, and both CDNs stay reachable.
        scriptSrc.Should().Contain("'wasm-unsafe-eval'");
        scriptSrc.Should().Contain("https://esm.sh");
        scriptSrc.Should().Contain("https://cdn.jsdelivr.net");
        // The relaxation is contained to the editor: the dashboard shell must stay free of inline script.
        DirectiveOf(context, "frame-ancestors").Should().Contain("'self'");
    }

    [Fact]
    public async Task InvokeAsync_DashboardPath_StillForbidsInlineScript()
    {
        SecurityHeadersMiddleware middleware = CreateMiddleware(NoOpNext, isDevelopment: false);
        DefaultHttpContext context = CreateContext("/");

        await middleware.InvokeAsync(context);

        DirectiveOf(context, "script-src").Should().NotContain("'unsafe-inline'");
    }

    private static string DirectiveOf(HttpContext context, string name) =>
        context
            .Response.Headers["Content-Security-Policy"]
            .ToString()
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Single(directive => directive.StartsWith($"{name} ", StringComparison.Ordinal));

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
