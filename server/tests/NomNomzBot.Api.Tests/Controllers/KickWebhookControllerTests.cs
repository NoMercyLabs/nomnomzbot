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
using NomNomzBot.Application.Contracts.Kick;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the Kick webhook gate: an unsigned/undersigned delivery never reaches the ingest (401); a
/// stale signed timestamp is rejected as a replay even with a VALID signature; a verified
/// <c>chat.message.sent</c> dispatches the raw body to the ingest; and a verified but unhandled event
/// type acknowledges 200 without dispatch (Kick must not retry what we deliberately ignore).
/// </summary>
public sealed class KickWebhookControllerTests
{
    private const string Body = """{"message_id":"m1"}""";

    private static (
        KickWebhookController Controller,
        IKickWebhookVerifier Verifier,
        IKickWebhookIngest Ingest
    ) Build(bool verifies = true)
    {
        IKickWebhookVerifier verifier = Substitute.For<IKickWebhookVerifier>();
        verifier
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(verifies);
        IKickWebhookIngest ingest = Substitute.For<IKickWebhookIngest>();

        KickWebhookController controller = new(verifier, ingest, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, verifier, ingest);
    }

    private static void SetDelivery(
        KickWebhookController controller,
        string eventType,
        string? timestamp = null,
        string body = Body
    )
    {
        HttpRequest request = controller.ControllerContext.HttpContext.Request;
        request.Headers["Kick-Event-Message-Id"] = "01JMSGID";
        request.Headers["Kick-Event-Message-Timestamp"] =
            timestamp ?? DateTimeOffset.UtcNow.ToString("O");
        request.Headers["Kick-Event-Signature"] = Convert.ToBase64String([1, 2, 3]);
        request.Headers["Kick-Event-Type"] = eventType;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task A_verified_chat_message_dispatches_the_raw_body_to_the_ingest()
    {
        (KickWebhookController controller, _, IKickWebhookIngest ingest) = Build();
        SetDelivery(controller, "chat.message.sent");

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<OkResult>();
        await ingest.Received(1).HandleChatMessageAsync(Body, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_signature_is_401_and_never_reaches_the_ingest()
    {
        (KickWebhookController controller, _, IKickWebhookIngest ingest) = Build(verifies: false);
        SetDelivery(controller, "chat.message.sent");

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await ingest
            .DidNotReceiveWithAnyArgs()
            .HandleChatMessageAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_signature_headers_are_401_without_any_verification_work()
    {
        (KickWebhookController controller, IKickWebhookVerifier verifier, _) = Build();
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(Body)
        );

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await verifier
            .DidNotReceiveWithAnyArgs()
            .VerifyAsync(default!, default!, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_signed_timestamp_is_rejected_as_a_replay_even_with_a_valid_signature()
    {
        (KickWebhookController controller, _, IKickWebhookIngest ingest) = Build(verifies: true);
        SetDelivery(
            controller,
            "chat.message.sent",
            timestamp: DateTimeOffset.UtcNow.AddHours(-1).ToString("O")
        );

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await ingest
            .DidNotReceiveWithAnyArgs()
            .HandleChatMessageAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_verified_livestream_status_dispatches_to_the_live_tracker()
    {
        (KickWebhookController controller, _, IKickWebhookIngest ingest) = Build();
        SetDelivery(controller, "livestream.status.updated");

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<OkResult>();
        await ingest.Received(1).HandleLivestreamStatusAsync(Body, Arg.Any<CancellationToken>());
        await ingest
            .DidNotReceiveWithAnyArgs()
            .HandleChatMessageAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unhandled_event_type_acknowledges_200_without_dispatch()
    {
        (KickWebhookController controller, _, IKickWebhookIngest ingest) = Build();
        SetDelivery(controller, "channel.followed");

        IActionResult result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<OkResult>("Kick must not retry an event we deliberately ignore");
        await ingest
            .DidNotReceiveWithAnyArgs()
            .HandleChatMessageAsync(default!, Arg.Any<CancellationToken>());
        await ingest
            .DidNotReceiveWithAnyArgs()
            .HandleLivestreamStatusAsync(default!, Arg.Any<CancellationToken>());
    }
}
