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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /channels/{id}/tts/overlay</c> hands out the one Audio page (<c>audio</c>) — the
/// page that actually plays TTS in OBS — and not the caption page, and that it provisions that widget on first
/// call for a fresh channel.
/// </summary>
public sealed class TtsConfigControllerOverlayUrlTests
{
    private const string AudioUrl = "https://bot.example.test/overlay/audio-token";
    private const string CaptionUrl = "https://bot.example.test/overlay/caption-token";
    private static readonly DateTime AudioLastRan = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static WidgetDetail Detail(string name, string url, DateTime? lastRanAt) =>
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
            ["tts_speak"],
            null,
            lastRanAt,
            DateTime.UtcNow,
            DateTime.UtcNow,
            false,
            false,
            false
        );

    private static TtsConfigController Build(IWidgetService widgetService) =>
        new(
            Substitute.For<ITtsConfigService>(),
            Substitute.For<ITtsLexiconService>(),
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ICurrentUserService>(),
            widgetService,
            Substitute.For<ITtsDispatchService>(),
            Substitute.For<IWidgetNotifier>()
        );

    private static IWidgetService WidgetsWithBothSurfaces()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(Arg.Any<string>(), "audio", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Detail("Audio", AudioUrl, AudioLastRan)));
        widgets
            .EnsureSystemWidgetAsync(Arg.Any<string>(), "tts_caption", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Detail("TTS Caption", CaptionUrl, null)));
        return widgets;
    }

    [Fact]
    public async Task GetOverlay_returns_the_audio_source_url_and_its_last_ran_time()
    {
        IWidgetService widgets = WidgetsWithBothSurfaces();
        TtsConfigController controller = Build(widgets);

        IActionResult result = await controller.GetOverlay("chan", CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        TtsOverlayDto data = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<TtsOverlayDto>>()
            .Subject.Data!;
        data.OverlayUrl.Should().Be(AudioUrl);
        data.LastRanAt.Should().Be(AudioLastRan);
    }

    [Fact]
    public async Task GetOverlay_provisions_the_audio_widget_and_never_the_caption_widget()
    {
        IWidgetService widgets = WidgetsWithBothSurfaces();
        TtsConfigController controller = Build(widgets);

        await controller.GetOverlay("fresh-channel", CancellationToken.None);

        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync("fresh-channel", "audio", Arg.Any<CancellationToken>());
        await widgets
            .DidNotReceive()
            .EnsureSystemWidgetAsync(
                Arg.Any<string>(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            );
    }
}
