// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S042: <c>GET /api/v1/templates/helpers?context=</c> returns the exact valid set for the requested
/// context — never everything regardless of context, and never a silent fallback for a bad context.
/// </summary>
public sealed class TemplatesControllerTests
{
    [Fact]
    public void Command_context_includes_args_and_user_helpers()
    {
        TemplatesController controller = new();

        IActionResult result = controller.GetHelpers("command");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<TemplateHelperDto>>>()
            .Subject;

        body.Data.Should().NotBeNull();
        body.Data!.Should().Contain(h => h.Key == "args.<n>");
        body.Data!.Should().Contain(h => h.Key == "user.name");
        body.Data!.Should()
            .BeEquivalentTo(
                TemplateHelperRegistry
                    .ForContext(TemplateHelperContext.Command)
                    .Select(TemplateHelperDto.FromEntry)
            );
    }

    [Fact]
    public void EventResponse_context_excludes_command_only_args_helper()
    {
        TemplatesController controller = new();

        IActionResult result = controller.GetHelpers("eventResponse");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().NotContain(h => h.Key == "args.<n>");
        body.Data!.Should().Contain(h => h.Key == "user.name");
    }

    [Fact]
    public void Timer_context_excludes_trigger_user_and_args_helpers()
    {
        TemplatesController controller = new();

        IActionResult result = controller.GetHelpers("timer");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().NotContain(h => h.Key == "args.<n>");
        body.Data!.Should().NotContain(h => h.Key == "user.name");
        body.Data!.Should().Contain(h => h.Key == "channel.display");
    }

    [Fact]
    public void Unknown_context_fails_honestly_instead_of_returning_everything()
    {
        TemplatesController controller = new();

        IActionResult result = controller.GetHelpers("not_a_real_context");

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
