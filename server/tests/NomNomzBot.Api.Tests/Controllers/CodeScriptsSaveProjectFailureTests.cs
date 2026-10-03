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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Platform.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A project save the build rejects answers 400 with the problems in <c>data.errors</c>, each with its file, line
/// and column, so the editor can underline the line. A failure whose service never asked for that stays exactly as
/// it was: no <c>data</c> on the wire.
/// </summary>
public sealed class CodeScriptsSaveProjectFailureTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<(int Status, JsonElement Body)> SaveWith(
        Result<CodeScriptVersionDto> failure
    )
    {
        ICodeScriptService scripts = Substitute.For<ICodeScriptService>();
        scripts
            .SaveProjectAsync(Arg.Any<Guid>(), Arg.Any<ProjectDto>(), Arg.Any<CancellationToken>())
            .Returns(failure);
        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .IsFeatureEnabledAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Guid.NewGuid());
        CodeScriptsController controller = new(
            scripts,
            Substitute.For<IScriptTestRunService>(),
            Substitute.For<ITriggerSampleCatalog>(),
            features,
            tenant
        );

        IActionResult result = await controller.SaveProject(
            Guid.NewGuid(),
            new(new Dictionary<string, string>(), new("index.ts", "script", "typescript", [])),
            default
        );

        ObjectResult objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        string json = JsonSerializer.Serialize(objectResult.Value, Wire);
        return (objectResult.StatusCode!.Value, JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public async Task A_rejected_project_save_puts_every_problem_with_its_position_in_data_errors()
    {
        Result<CodeScriptVersionDto> failure = Result.Failure<CodeScriptVersionDto>(
            "lib.ts:2:18: Unexpected \";\"",
            "VALIDATION_FAILED",
            errorData: new ProjectBuildFailure([
                new("build", "lib.ts:2:18: Unexpected \";\"", "lib.ts", 2, 18),
                new("build", "The script build failed.", null, null, null),
            ])
        );

        (int status, JsonElement body) = await SaveWith(failure);

        status.Should().Be(400);
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        JsonElement errors = body.GetProperty("data").GetProperty("errors");
        errors.GetArrayLength().Should().Be(2);
        errors[0].GetProperty("code").GetString().Should().Be("build");
        errors[0].GetProperty("message").GetString().Should().Be("lib.ts:2:18: Unexpected \";\"");
        errors[0].GetProperty("file").GetString().Should().Be("lib.ts");
        errors[0].GetProperty("line").GetInt32().Should().Be(2);
        errors[0].GetProperty("column").GetInt32().Should().Be(18);
        errors[1].TryGetProperty("line", out _).Should().BeFalse("an unknown position is absent");
    }

    [Fact]
    public async Task A_failure_with_error_data_the_service_did_not_expose_keeps_its_old_body()
    {
        Result<CodeScriptVersionDto> failure = Result.Failure<CodeScriptVersionDto>(
            "Too many requests",
            "VALIDATION_FAILED",
            errorData: new MusicRequestRefusal(Limit: 3)
        );

        (int status, JsonElement body) = await SaveWith(failure);

        status.Should().Be(400);
        body.TryGetProperty("data", out _).Should().BeFalse();
        body.GetProperty("message").GetString().Should().Be("Too many requests");
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }
}
