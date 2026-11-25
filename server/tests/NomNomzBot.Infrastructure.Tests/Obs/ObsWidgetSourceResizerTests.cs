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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

public sealed class ObsWidgetSourceResizerTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();

    [Fact]
    public async Task A_new_design_size_resizes_only_the_sources_of_that_widget()
    {
        Guid widgetId = Guid.NewGuid();
        IObsControlService obs = Substitute.For<IObsControlService>();
        List<ObsRequest> sent = [];
        obs.GetBrowserSourcesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsBrowserSourceDto>>([
                    new(
                        "Now Playing",
                        $"https://bot.test/overlay?widgetId={widgetId}&token=t",
                        true,
                        true
                    ),
                    new(
                        "Other",
                        $"https://bot.test/overlay?widgetId={Guid.NewGuid()}&token=t",
                        true,
                        true
                    ),
                ])
            );
        obs.RequestAsync(Broadcaster, Arg.Do<ObsRequest>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsResponse>("unused"));

        await new ObsWidgetSourceResizer(obs).HandleAsync(Changed(widgetId, 825, 375));

        sent.Should().ContainSingle();
        sent[0].RequestType.Should().Be("SetInputSettings");
        sent[0].RequestData!["inputName"].Should().Be("Now Playing");
        IReadOnlyDictionary<string, object?> size =
            (IReadOnlyDictionary<string, object?>)sent[0].RequestData!["inputSettings"]!;
        size["width"].Should().Be(825);
        size["height"].Should().Be(375);
    }

    [Fact]
    public async Task Settings_without_a_design_size_leave_OBS_alone()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();

        await new ObsWidgetSourceResizer(obs).HandleAsync(
            new()
            {
                BroadcasterId = Broadcaster,
                WidgetId = Guid.NewGuid(),
                Settings = new Dictionary<string, object>(),
            }
        );

        await obs.DidNotReceiveWithAnyArgs().GetBrowserSourcesAsync(default, default);
    }

    private static WidgetSettingsChangedEvent Changed(Guid widgetId, int width, int height) =>
        new()
        {
            BroadcasterId = Broadcaster,
            WidgetId = widgetId,
            Settings = new Dictionary<string, object>
            {
                ["designWidth"] = width,
                ["designHeight"] = height,
            },
        };
}
