// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.Tests.Sandbox;

/// <summary>
/// Proves the egress scheme gate (code-execution-sandbox.md §7.1 step 1): the egress client refuses any non-https
/// request before it reaches the wire, while https passes through to the inner handler.
/// </summary>
public sealed class EgressSchemeHandlerTests
{
    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Rejects_a_non_https_request()
    {
        HttpMessageInvoker invoker = new(
            new EgressSchemeHandler { InnerHandler = new OkHandler() }
        );

        Func<Task> act = () =>
            invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "http://example.com/hook"),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Allows_an_https_request_through()
    {
        HttpMessageInvoker invoker = new(
            new EgressSchemeHandler { InnerHandler = new OkHandler() }
        );

        HttpResponseMessage response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/hook"),
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
