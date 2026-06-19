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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Middleware;

namespace NomNomzBot.Api.Tests.Middleware;

public class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsXRequestIdHeader()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        RequestLoggingMiddleware middleware = new(
            next,
            NullLogger<RequestLoggingMiddleware>.Instance
        );
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context);

        context.Response.Headers.ContainsKey("X-Request-Id").Should().BeTrue();
        context.Response.Headers["X-Request-Id"].ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        bool nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        RequestLoggingMiddleware middleware = new(
            next,
            NullLogger<RequestLoggingMiddleware>.Instance
        );
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_XRequestId_IsShortGuid()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        RequestLoggingMiddleware middleware = new(
            next,
            NullLogger<RequestLoggingMiddleware>.Instance
        );
        DefaultHttpContext context = new();

        await middleware.InvokeAsync(context);

        string requestId = context.Response.Headers["X-Request-Id"].ToString();
        requestId.Should().HaveLength(8);
    }

    [Fact]
    public async Task InvokeAsync_EachRequest_HasUniqueRequestId()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        RequestLoggingMiddleware middleware = new(
            next,
            NullLogger<RequestLoggingMiddleware>.Instance
        );

        DefaultHttpContext ctx1 = new();
        DefaultHttpContext ctx2 = new();

        await middleware.InvokeAsync(ctx1);
        await middleware.InvokeAsync(ctx2);

        string id1 = ctx1.Response.Headers["X-Request-Id"].ToString();
        string id2 = ctx2.Response.Headers["X-Request-Id"].ToString();

        id1.Should().NotBe(id2);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_PropagatesException()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("test error");
        RequestLoggingMiddleware middleware = new(
            next,
            NullLogger<RequestLoggingMiddleware>.Instance
        );
        DefaultHttpContext context = new();

        Func<Task> act = () => middleware.InvokeAsync(context);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test error");
    }
}
