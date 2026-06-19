// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Middleware;
using NSubstitute;
// IHostEnvironment is in Microsoft.Extensions.Hosting.Abstractions
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostEnvironment;

namespace NomNomzBot.Api.Tests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    private static GlobalExceptionMiddleware CreateMiddleware(
        RequestDelegate next,
        bool isDevelopment = false
    )
    {
        IHostEnvironment? env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return new(
            next,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            env
        );
    }

    private static DefaultHttpContext CreateContext()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        bool nextCalled = false;
        GlobalExceptionMiddleware middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200); // unchanged
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500()
    {
        GlobalExceptionMiddleware middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_SetsJsonContentType()
    {
        GlobalExceptionMiddleware middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Contain("application/json");
    }

    [Fact]
    public async Task InvokeAsync_Production_ReturnsGenericMessage()
    {
        GlobalExceptionMiddleware middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("secret internal error"),
            isDevelopment: false
        );

        DefaultHttpContext context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        body.Should().NotContain("secret internal error");
        body.Should().Contain("unexpected error");
    }

    [Fact]
    public async Task InvokeAsync_Development_ReturnsActualMessage()
    {
        GlobalExceptionMiddleware middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("detailed dev error"),
            isDevelopment: true
        );

        DefaultHttpContext context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        body.Should().Contain("detailed dev error");
    }

    [Fact]
    public async Task InvokeAsync_RequestCancelled_DoesNotReturn500()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        GlobalExceptionMiddleware middleware = CreateMiddleware(_ => throw new OperationCanceledException("cancelled"));
        DefaultHttpContext context = CreateContext();
        context.RequestAborted = cts.Token;

        await middleware.InvokeAsync(context);

        // Should silently swallow OperationCanceledException when request is aborted
        context.Response.StatusCode.Should().Be(200); // not 500
    }

    [Fact]
    public async Task InvokeAsync_ResponseBody_IsValidJson()
    {
        GlobalExceptionMiddleware middleware = CreateMiddleware(_ => throw new("test"), isDevelopment: false);
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Func<JsonDocument> act = () => JsonDocument.Parse(body);
        act.Should().NotThrow("response body should be valid JSON");
    }
}
