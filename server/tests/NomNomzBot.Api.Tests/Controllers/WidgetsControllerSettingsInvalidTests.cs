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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Saving a widget project whose <c>settings.json</c> is invalid is the author's mistake, so it answers a 4xx
/// with the machine-readable code the editor shows, never a 500.
/// </summary>
public sealed class WidgetsControllerSettingsInvalidTests
{
    [Fact]
    public async Task SaveWidgetProject_with_invalid_settings_json_answers_400_with_WIDGET_SETTINGS_INVALID()
    {
        Guid widgetGuid = Guid.CreateVersion7();
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .SaveProjectAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ProjectDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<WidgetVersionDetail>(
                    "field \"volume\": type \"slider\" is not allowed.",
                    "WIDGET_SETTINGS_INVALID"
                )
            );
        WidgetsController controller = new(service, new ConfigurationBuilder().Build());
        controller.ControllerContext = new();

        ProjectDto project = new(
            new() { ["settings.json"] = "{ \"fields\": [ { \"key\": \"volume\" } ] }" },
            new("index.html", "widget", "html", null)
        );
        IActionResult response = await controller.SaveWidgetProject(
            "chan",
            widgetGuid.ToString(),
            project,
            CancellationToken.None
        );

        ObjectResult objectResult = response.Should().BeOfType<BadRequestObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(400);
        StatusResponseDto<object> body = objectResult
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Code.Should().Be("WIDGET_SETTINGS_INVALID");
        body.Message.Should().Contain("volume");
    }
}
