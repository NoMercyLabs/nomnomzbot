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
using NomNomzBot.Infrastructure.Content.Widgets;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /channels/{id}/overlays/required</c> (widgets-overlays.md §1.2) provisions every system surface of
/// the channel and hands the OBS page one list: each surface keyed by its catalogue key, with the OBS URL on the
/// origin the operator reached the dashboard on.
/// </summary>
public sealed class WidgetsControllerRequiredOverlaysTests
{
    private const string Channel = "chan";

    private static WidgetDetail Detail(string name, string url) =>
        new(
            Guid.CreateVersion7(),
            name,
            null,
            "vue",
            "first_party",
            true,
            url,
            null,
            null,
            new(),
            [],
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            false,
            false,
            false
        );

    private static WidgetsController Build(IWidgetService widgets) =>
        new(
            widgets,
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["OverlayBaseUrl"] = "https://obs.example.test/",
                    }
                )
                .Build()
        );

    [Fact]
    public async Task GetRequiredOverlays_provisions_every_system_surface_and_returns_them_in_catalogue_order()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success(
                    Detail(
                        (string)call[1],
                        $"http://localhost:8080/overlay/{(string)call[1]}?token=t"
                    )
                )
            );

        IActionResult result = await Build(widgets)
            .GetRequiredOverlays(Channel, CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        List<RequiredOverlay> data = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<RequiredOverlay>>>()
            .Subject.Data!;
        data.Select(d => d.Key).Should().Equal(FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys);
        data.Select(d => d.Widget.Name)
            .Should()
            .Equal(FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys);
        data.Should()
            .OnlyContain(d =>
                d.Widget.OverlayUrl!.StartsWith("https://obs.example.test/overlay/")
                && d.Widget.OverlayUrl.EndsWith("?token=t")
            );
    }

    [Fact]
    public async Task GetRequiredOverlays_never_lists_or_provisions_the_raid_recap_a_channel_adds_itself()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(Detail((string)call[1], "http://x/overlay/a?token=t")));

        IActionResult result = await Build(widgets)
            .GetRequiredOverlays(Channel, CancellationToken.None);

        List<RequiredOverlay> data = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<StatusResponseDto<List<RequiredOverlay>>>()
            .Subject.Data!;
        data.Select(d => d.Key).Should().NotContain("raid_recap");
        await widgets
            .DidNotReceive()
            .EnsureSystemWidgetAsync(Channel, "raid_recap", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRequiredOverlays_provisions_the_media_share_player_so_shared_media_can_play_on_stream()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(Detail((string)call[1], "http://x/overlay/a?token=t")));

        IActionResult result = await Build(widgets)
            .GetRequiredOverlays(Channel, CancellationToken.None);

        List<RequiredOverlay> data = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<StatusResponseDto<List<RequiredOverlay>>>()
            .Subject.Data!;
        data.Select(d => d.Key).Should().Contain("media_share");
        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync(Channel, "media_share", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRequiredOverlays_reports_the_failure_when_a_surface_cannot_be_provisioned()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WidgetDetail>("missing", "NOT_FOUND"));

        IActionResult result = await Build(widgets)
            .GetRequiredOverlays(Channel, CancellationToken.None);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
