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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Infrastructure.Discord.Interactions;
using NSec.Cryptography;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the interactions webhook's security boundary with a REAL Ed25519 keypair against the REAL verifier:
/// a valid signature over <c>timestamp + raw body</c> reaches the handler and returns its interaction-response
/// JSON; a tampered body, a signature from another key, garbage signature material, or missing headers all
/// answer 401 WITHOUT the handler ever being invoked (Discord probes with invalid signatures); an unconfigured
/// <c>Discord:PublicKey</c> answers 503 and never throws.
/// </summary>
public sealed class DiscordInteractionsControllerTests
{
    private const string Timestamp = "1751673600";
    private const string PingBody = """{"type":1}""";
    private const string PongJson = """{"type":1}""";

    [Fact]
    public async Task Post_ValidSignature_ReachesHandler_AndReturnsItsInteractionResponseJson()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        IDiscordInteractionService interactions = HandlerReturning(Result.Success(PongJson));
        DiscordInteractionsController controller = Controller(key, interactions);
        SetRequest(controller, PingBody, Sign(key, Timestamp, PingBody), Timestamp);

        IActionResult result = await controller.Receive(default);

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be(PongJson); // the exact PONG handshake JSON
        content.ContentType.Should().Be("application/json");

        // The handler received the exact raw body the signature covered.
        await interactions.Received(1).HandleAsync(PingBody, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_TamperedBody_Returns401_WithoutReachingHandler()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        IDiscordInteractionService interactions = HandlerReturning(Result.Success(PongJson));
        DiscordInteractionsController controller = Controller(key, interactions);

        // Signed over the PING body, delivered with a different body.
        SetRequest(controller, """{"type":3}""", Sign(key, Timestamp, PingBody), Timestamp);

        IActionResult result = await controller.Receive(default);

        result.Should().BeOfType<UnauthorizedResult>();
        await interactions
            .DidNotReceiveWithAnyArgs()
            .HandleAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_SignatureFromWrongKey_Returns401_WithoutReachingHandler()
    {
        using Key configured = Key.Create(SignatureAlgorithm.Ed25519);
        using Key attacker = Key.Create(SignatureAlgorithm.Ed25519);
        IDiscordInteractionService interactions = HandlerReturning(Result.Success(PongJson));
        DiscordInteractionsController controller = Controller(configured, interactions);
        SetRequest(controller, PingBody, Sign(attacker, Timestamp, PingBody), Timestamp);

        IActionResult result = await controller.Receive(default);

        result.Should().BeOfType<UnauthorizedResult>();
        await interactions
            .DidNotReceiveWithAnyArgs()
            .HandleAsync(default!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, Timestamp)] // missing signature header
    [InlineData("", Timestamp)] // empty signature header
    [InlineData("deadbeef", null)] // missing timestamp header
    [InlineData("not-even-hex-material-here", Timestamp)] // stale garbage signature
    public async Task Post_MissingOrGarbageHeaders_Returns401_WithoutReachingHandler(
        string? signature,
        string? timestamp
    )
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        IDiscordInteractionService interactions = HandlerReturning(Result.Success(PongJson));
        DiscordInteractionsController controller = Controller(key, interactions);
        SetRequest(controller, PingBody, signature, timestamp);

        IActionResult result = await controller.Receive(default);

        result.Should().BeOfType<UnauthorizedResult>();
        await interactions
            .DidNotReceiveWithAnyArgs()
            .HandleAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_UnconfiguredPublicKey_Returns503_AndNeverThrows()
    {
        // No Discord:PublicKey configured — the feature is off; Discord's probe gets a clean 503.
        DiscordInteractionVerifier verifier = new(ConfigWith(null));
        IDiscordInteractionService interactions = HandlerReturning(Result.Success(PongJson));
        DiscordInteractionsController controller = new(verifier, interactions)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        SetRequest(controller, PingBody, Sign(key, Timestamp, PingBody), Timestamp);

        IActionResult result = await controller.Receive(default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);
        await interactions
            .DidNotReceiveWithAnyArgs()
            .HandleAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_ValidSignature_UnparseableInteraction_Returns400()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        IDiscordInteractionService interactions = HandlerReturning(
            Result.Failure<string>(
                "The interaction payload is not valid JSON.",
                "VALIDATION_FAILED"
            )
        );
        DiscordInteractionsController controller = Controller(key, interactions);
        const string body = "not-json-but-signed";
        SetRequest(controller, body, Sign(key, Timestamp, body), Timestamp);

        IActionResult result = await controller.Receive(default);

        result.Should().BeOfType<BadRequestResult>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordInteractionsController Controller(
        Key key,
        IDiscordInteractionService interactions
    )
    {
        DiscordInteractionVerifier verifier = new(
            ConfigWith(Convert.ToHexString(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)))
        );
        return new DiscordInteractionsController(verifier, interactions)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static IDiscordInteractionService HandlerReturning(Result<string> reply)
    {
        IDiscordInteractionService interactions = Substitute.For<IDiscordInteractionService>();
        interactions.HandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(reply);
        return interactions;
    }

    private static IConfiguration ConfigWith(string? publicKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Discord:PublicKey"] = publicKey }
            )
            .Build();

    private static void SetRequest(
        DiscordInteractionsController controller,
        string body,
        string? signature,
        string? timestamp
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        controller.Request.Body = new MemoryStream(bytes);
        controller.Request.ContentLength = bytes.Length;
        controller.Request.ContentType = "application/json";
        if (signature is not null)
            controller.Request.Headers["X-Signature-Ed25519"] = signature;
        if (timestamp is not null)
            controller.Request.Headers["X-Signature-Timestamp"] = timestamp;
    }

    private static string Sign(Key key, string timestamp, string body)
    {
        byte[] message = Encoding.UTF8.GetBytes(timestamp + body);
        return Convert
            .ToHexString(SignatureAlgorithm.Ed25519.Sign(key, message))
            .ToLowerInvariant();
    }
}
