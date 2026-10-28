// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay <c>template-render</c> read is what the widget SDK's <c>template.render</c> calls: the token picks
/// the channel, the values seed the render, and the text comes back from the one resolver.
/// </summary>
public sealed class OverlayControllerTemplateRenderTests
{
    private static readonly Guid Channel = Guid.Parse("0192f000-0000-7000-8000-0000000000a1");

    private static (OverlayController Controller, ITemplateResolver Templates) Create()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .ResolveBroadcasterIdByOverlayTokenAsync("good-token", Arg.Any<CancellationToken>())
            .Returns(Channel);
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        OverlayController controller = new(
            widgets,
            Substitute.For<IStreamStatsService>(),
            templates
        )
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return (controller, templates);
    }

    [Fact]
    public async Task The_template_and_values_reach_the_resolver_for_the_token_channel_and_its_text_comes_back()
    {
        (OverlayController controller, ITemplateResolver templates) = Create();
        templates
            .ResolveAsync(
                "{text.upper:{name}}",
                Arg.Is<IDictionary<string, string>>(s => s["name"] == "hi" && s["n"] == "3"),
                Channel,
                Arg.Any<CancellationToken>()
            )
            .Returns("HI");

        IActionResult result = await controller.RenderTemplate(
            "{text.upper:{name}}",
            """{"name":"hi","n":3}""",
            "good-token",
            CancellationToken.None
        );

        StatusResponseDto<string> body = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<StatusResponseDto<string>>()
            .Subject;
        body.Data.Should().Be("HI");
    }

    [Fact]
    public async Task An_oversized_template_is_a_400_and_nothing_is_rendered()
    {
        (OverlayController controller, ITemplateResolver templates) = Create();

        IActionResult result = await controller.RenderTemplate(
            new string('x', 5000),
            null,
            "good-token",
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestObjectResult>();
        await templates
            .DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task An_unknown_token_is_a_404()
    {
        (OverlayController controller, _) = Create();

        IActionResult result = await controller.RenderTemplate(
            "{text.upper:a}",
            null,
            "bad-token",
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
