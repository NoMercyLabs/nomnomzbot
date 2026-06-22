// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the public ingest controller (webhooks.md §5.2): an unsupported content-type is 415, an oversized body is
/// 413 (both before dispatch), and an accepted request maps the dispatcher's typed status (200 ack / 404 unknown)
/// straight through as a bare status code — never problem-details.
/// </summary>
public sealed class InboundWebhookControllerTests
{
    private static (InboundWebhookController Sut, IInboundWebhookDispatcher Dispatcher) Build(
        string contentType,
        byte[] body
    )
    {
        IInboundWebhookDispatcher dispatcher = Substitute.For<IInboundWebhookDispatcher>();
        InboundWebhookController sut = new(dispatcher, TimeProvider.System);
        DefaultHttpContext httpContext = new();
        httpContext.Request.ContentType = contentType;
        httpContext.Request.Body = new MemoryStream(body);
        httpContext.Request.ContentLength = body.Length;
        sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (sut, dispatcher);
    }

    private static InboundDispatchResult Outcome(int httpStatus) =>
        new(
            httpStatus == 200,
            false,
            null,
            0,
            "webhook.github.push",
            httpStatus == 200 ? null : WebhookRejectReason.UnknownEndpoint,
            httpStatus,
            null,
            null,
            WebhookAdapterKind.Github
        );

    [Fact]
    public async Task An_unsupported_content_type_is_415()
    {
        (InboundWebhookController sut, _) = Build("text/plain", Encoding.UTF8.GetBytes("{}"));

        IActionResult result = await sut.Receive("tok", default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(415);
    }

    [Fact]
    public async Task An_oversized_body_is_413()
    {
        (InboundWebhookController sut, _) = Build("application/json", new byte[256 * 1024 + 1]);

        IActionResult result = await sut.Receive("tok", default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task An_accepted_request_maps_the_dispatcher_status()
    {
        (InboundWebhookController sut, IInboundWebhookDispatcher dispatcher) = Build(
            "application/json",
            Encoding.UTF8.GetBytes("{\"a\":1}")
        );
        dispatcher
            .DispatchAsync(Arg.Any<InboundWebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(NomNomzBot.Application.Common.Models.Result.Success(Outcome(200)));

        IActionResult result = await sut.Receive("tok123", default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task An_unknown_token_maps_to_404()
    {
        (InboundWebhookController sut, IInboundWebhookDispatcher dispatcher) = Build(
            "application/json",
            Encoding.UTF8.GetBytes("{}")
        );
        dispatcher
            .DispatchAsync(Arg.Any<InboundWebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(NomNomzBot.Application.Common.Models.Result.Success(Outcome(404)));

        IActionResult result = await sut.Receive("nope", default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(404);
    }
}
