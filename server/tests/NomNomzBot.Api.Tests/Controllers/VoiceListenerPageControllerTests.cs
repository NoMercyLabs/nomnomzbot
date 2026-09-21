// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

public sealed class VoiceListenerPageControllerTests
{
    [Fact]
    public void Get_AllowsOnlyItsNonceBearingInlineScript()
    {
        (VoiceListenerPageController firstController, DefaultHttpContext firstContext) =
            CreateController();
        (VoiceListenerPageController secondController, DefaultHttpContext secondContext) =
            CreateController();

        ContentResult first = firstController
            .Get("listener-token")
            .Should()
            .BeOfType<ContentResult>()
            .Subject;
        secondController.Get("listener-token");

        string firstPolicy = firstContext.Response.Headers["Content-Security-Policy"].ToString();
        string secondPolicy = secondContext.Response.Headers["Content-Security-Policy"].ToString();
        string firstNonce = ExtractNonce(firstPolicy);
        string secondNonce = ExtractNonce(secondPolicy);

        firstNonce.Should().NotBeNullOrEmpty();
        DirectiveOf(firstPolicy, "script-src")
            .Should()
            .NotContain(
                "'unsafe-inline'",
                "inline JavaScript should be limited to the generated nonce"
            );
        first.Content.Should().Contain($"<script nonce=\"{firstNonce}\">");
        secondNonce
            .Should()
            .NotBe(firstNonce, "a fresh nonce must be generated for every response");
    }

    [Fact]
    public void Get_ForbidsFramingSoTheListenerTokenCannotBeClickjacked()
    {
        (VoiceListenerPageController controller, DefaultHttpContext context) = CreateController();

        controller.Get("listener-token");

        string policy = context.Response.Headers["Content-Security-Policy"].ToString();
        DirectiveOf(policy, "frame-ancestors").Should().Be("frame-ancestors 'none'");
    }

    [Fact]
    public void Get_ReGrantsTheMicrophoneTheBaselineHeadersSwitchOff()
    {
        (VoiceListenerPageController controller, DefaultHttpContext context) = CreateController();
        context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        controller.Get("listener-token");

        string policy = context.Response.Headers["Permissions-Policy"].ToString();
        policy.Should().Contain("microphone=(self)", "speech recognition cannot start without it");
        policy.Should().Contain("geolocation=()").And.Contain("camera=()");
    }

    private static (
        VoiceListenerPageController Controller,
        DefaultHttpContext Context
    ) CreateController()
    {
        DefaultHttpContext context = new();
        VoiceListenerPageController controller = new()
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        return (controller, context);
    }

    private static string ExtractNonce(string policy)
    {
        Match match = Regex.Match(policy, "script-src 'self' 'nonce-([^']+)'");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string DirectiveOf(string policy, string name) =>
        policy
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Single(directive => directive.StartsWith($"{name} ", StringComparison.Ordinal));
}
