// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A widget project save the build rejects answers 400 with the problems in <c>data.errors</c>, each with its file,
/// line and column, so the editor can underline the line. The code and the message stay as they were.
/// </summary>
public sealed class WidgetsSaveProjectFailureTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<(int Status, JsonElement Body)> SaveWith(
        Result<WidgetVersionDetail> failure
    )
    {
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .SaveProjectAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ProjectDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(failure);
        WidgetsController controller = new(service, new ConfigurationBuilder().Build())
        {
            ControllerContext = new(),
        };

        IActionResult result = await controller.SaveWidgetProject(
            "chan",
            Guid.CreateVersion7().ToString(),
            new(new(), new("index.tsx", "widget", "react", [])),
            CancellationToken.None
        );

        ObjectResult objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        string json = JsonSerializer.Serialize(objectResult.Value, Wire);
        return (objectResult.StatusCode!.Value, JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public async Task A_rejected_widget_save_puts_every_problem_with_its_position_in_data_errors()
    {
        Result<WidgetVersionDetail> failure = Result.Failure<WidgetVersionDetail>(
            "lib/util.ts:2:18: Unexpected \";\"",
            "VALIDATION_FAILED",
            errorData: new ProjectBuildFailure([
                new("WIDGET_BUILD_FAILED", "Unexpected \";\"", "lib/util.ts", 2, 18),
                new("WIDGET_PROJECT_PATH_INVALID", "not a safe path", "../x.js", null, null),
            ])
        );

        (int status, JsonElement body) = await SaveWith(failure);

        status.Should().Be(400);
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        body.GetProperty("message").GetString().Should().Be("lib/util.ts:2:18: Unexpected \";\"");
        JsonElement errors = body.GetProperty("data").GetProperty("errors");
        errors.GetArrayLength().Should().Be(2);
        errors[0].GetProperty("code").GetString().Should().Be("WIDGET_BUILD_FAILED");
        errors[0].GetProperty("file").GetString().Should().Be("lib/util.ts");
        errors[0].GetProperty("line").GetInt32().Should().Be(2);
        errors[0].GetProperty("column").GetInt32().Should().Be(18);
        errors[1].GetProperty("file").GetString().Should().Be("../x.js");
        errors[1].TryGetProperty("line", out _).Should().BeFalse("an unknown position is absent");
    }

    [Fact]
    public async Task A_widget_failure_without_exposed_error_data_keeps_its_old_body()
    {
        (int status, JsonElement body) = await SaveWith(
            Result.Failure<WidgetVersionDetail>(
                "The widget project failed to build.",
                "VALIDATION_FAILED"
            )
        );

        status.Should().Be(400);
        body.TryGetProperty("data", out _).Should().BeFalse();
        body.GetProperty("message").GetString().Should().Be("The widget project failed to build.");
    }
}
