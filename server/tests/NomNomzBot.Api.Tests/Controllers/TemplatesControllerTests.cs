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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Templating;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S042: <c>GET /api/v1/templates/helpers?context=</c> returns the exact valid set for the requested
/// context — never everything regardless of context, and never a silent fallback for a bad context.
/// </summary>
public sealed class TemplatesControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000e001");
    private static readonly Guid SignedInUser = Guid.Parse("0192b400-0000-7000-8000-00000000e002");

    /// <summary>A controller whose renderer returns its input unchanged (the neutral registry samples).</summary>
    private static TemplatesController Build()
    {
        ITemplateSampleRenderer renderer = Substitute.For<ITemplateSampleRenderer>();
        renderer
            .PersonalizeAsync(
                Arg.Any<IReadOnlyList<TemplateHelperEntry>>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.Arg<IReadOnlyList<TemplateHelperEntry>>());

        return Build(renderer);
    }

    private static TemplatesController Build(ITemplateSampleRenderer renderer)
    {
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(SignedInUser.ToString());
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);

        return new TemplatesController(renderer, user, tenant);
    }

    /// <summary>
    /// The picker's examples are rendered for the requester: the controller hands the current channel and
    /// the signed-in user to the renderer, and ships back what it returns.
    /// </summary>
    [Fact]
    public async Task Examples_are_rendered_for_the_signed_in_user_and_the_current_channel()
    {
        ITemplateSampleRenderer renderer = Substitute.For<ITemplateSampleRenderer>();
        renderer
            .PersonalizeAsync(
                Arg.Any<IReadOnlyList<TemplateHelperEntry>>(),
                Channel,
                SignedInUser,
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                (IReadOnlyList<TemplateHelperEntry>)
                    [
                        .. call.Arg<IReadOnlyList<TemplateHelperEntry>>()
                            .Select(e => e with { Sample = "real:" + e.Key }),
                    ]
            );

        IActionResult result = await Build(renderer).GetHelpers("command");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;
        body.Data!.Should().OnlyContain(h => h.Sample == "real:" + h.Key);
    }

    [Fact]
    public async Task Command_context_includes_args_and_user_helpers()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("command");

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
    public async Task EventResponse_context_excludes_command_only_args_helper()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("eventResponse");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().NotContain(h => h.Key == "args.<n>");
        body.Data!.Should().Contain(h => h.Key == "user.name");
    }

    [Fact]
    public async Task Timer_context_excludes_trigger_user_and_args_helpers()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("timer");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().NotContain(h => h.Key == "args.<n>");
        body.Data!.Should().NotContain(h => h.Key == "user.name");
        body.Data!.Should().Contain(h => h.Key == "channel.display");
    }

    [Fact]
    public async Task Unknown_context_fails_honestly_instead_of_returning_everything()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("not_a_real_context");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// S-OWN16: an EventResponse editor for a raid must not offer subscription-only helpers like
    /// <c>tier</c>/<c>months</c>/<c>streak</c> — they will always resolve empty on a raid event. Global
    /// helpers (channel identity, time, the triggering user) stay available regardless of event type.
    /// </summary>
    [Fact]
    public async Task EventType_filter_excludes_helpers_seeded_by_unrelated_events()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("eventResponse", "channel.raid");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().Contain(h => h.Key == "viewers"); // raid's own helper
        body.Data!.Should().Contain(h => h.Key == "user.name"); // global trigger-user helper
        body.Data!.Should().Contain(h => h.Key == "channel.display"); // global channel helper
        body.Data!.Should().NotContain(h => h.Key == "tier"); // subscription-only
        body.Data!.Should().NotContain(h => h.Key == "months"); // subscription-only
        body.Data!.Should().NotContain(h => h.Key == "streak"); // subscription-only
        body.Data!.Should().NotContain(h => h.Key == "bits"); // cheer-only
        body.Data!.Should().NotContain(h => h.Key == "reward"); // redemption-only
    }

    /// <summary>
    /// The counterpart to the raid check above: a subscription-message editor gets subscription helpers
    /// but not the raid-only <c>viewers</c> helper.
    /// </summary>
    [Fact]
    public async Task EventType_filter_swaps_the_scoped_set_per_event()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers(
            "eventResponse",
            "channel.subscription.message"
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<TemplateHelperDto>> body =
            (StatusResponseDto<IReadOnlyList<TemplateHelperDto>>)ok.Value!;

        body.Data!.Should().Contain(h => h.Key == "tier");
        body.Data!.Should().Contain(h => h.Key == "months");
        body.Data!.Should().Contain(h => h.Key == "streak");
        body.Data!.Should().NotContain(h => h.Key == "viewers");
    }

    [Fact]
    public async Task Unknown_eventType_fails_honestly_instead_of_returning_everything()
    {
        TemplatesController controller = Build();

        IActionResult result = await controller.GetHelpers("eventResponse", "not_a_real_event");

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
