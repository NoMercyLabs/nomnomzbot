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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// An overlay that declares no settings is a normal answer for the dashboard's settings panel: it shows the empty
/// state. Stoney 2026-11-17: the dashboard got a 404 for it, retried, and filled his console with ten red lines.
/// </summary>
public sealed class WidgetsControllerSettingsSchemaTests
{
    [Fact]
    public async Task A_widget_without_a_settings_schema_answers_200_with_no_fields()
    {
        Guid widgetGuid = Guid.CreateVersion7();
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .GetSettingsSchemaAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<WidgetSettingsSchema>(
                    "This widget declares no settings.",
                    "WIDGET_NO_SETTINGS_SCHEMA"
                )
            );
        WidgetsController controller = new(service, new ConfigurationBuilder().Build());
        controller.ControllerContext = new();

        IActionResult response = await controller.GetWidgetSettingsSchema(
            "chan",
            widgetGuid.ToString(),
            CancellationToken.None
        );

        OkObjectResult ok = response.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<WidgetSettingsSchema> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<WidgetSettingsSchema>>()
            .Subject;
        body.Data!.Fields.Should().BeEmpty();
        body.Data.EventSubscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_widget_still_answers_404()
    {
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .GetSettingsSchemaAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Errors.NotFound<WidgetSettingsSchema>("Widget", "w-1"));
        WidgetsController controller = new(service, new ConfigurationBuilder().Build());
        controller.ControllerContext = new();

        IActionResult response = await controller.GetWidgetSettingsSchema(
            "chan",
            Guid.CreateVersion7().ToString(),
            CancellationToken.None
        );

        response.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(404);
    }
}
