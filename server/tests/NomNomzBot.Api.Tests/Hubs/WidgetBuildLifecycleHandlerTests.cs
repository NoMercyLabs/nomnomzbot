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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Widgets.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the build lifecycle drives the overlay: a successful build reloads the widget's overlay group (this is
/// what makes compile-on-save hot-swap the on-stream widget), and a failed build pushes a compile-failed notice
/// carrying the version number + error — each without triggering the other.
/// </summary>
public sealed class WidgetBuildLifecycleHandlerTests
{
    [Fact]
    public async Task Succeeded_reloads_the_widget_overlay_and_does_not_push_compile_failed()
    {
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetBuildLifecycleHandler handler = new(notifier);
        Guid broadcaster = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();

        await handler.HandleAsync(
            new WidgetBuildSucceededEvent
            {
                BroadcasterId = broadcaster,
                WidgetId = widget,
                VersionId = Guid.CreateVersion7(),
                VersionNumber = 3,
                ContentHash = "hash",
            }
        );

        await notifier
            .Received(1)
            .ReloadWidgetAsync(
                broadcaster.ToString(),
                widget.ToString(),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .DidNotReceive()
            .SendCompileFailedAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WidgetCompileFailedDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Failed_pushes_compile_failed_with_the_error_and_does_not_reload()
    {
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetBuildLifecycleHandler handler = new(notifier);
        Guid broadcaster = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();

        await handler.HandleAsync(
            new WidgetBuildFailedEvent
            {
                BroadcasterId = broadcaster,
                WidgetId = widget,
                VersionId = Guid.CreateVersion7(),
                VersionNumber = 5,
                BuildError = "Unexpected token }",
            }
        );

        await notifier
            .Received(1)
            .SendCompileFailedAsync(
                broadcaster.ToString(),
                widget.ToString(),
                Arg.Is<WidgetCompileFailedDto>(d =>
                    d.WidgetId == widget.ToString()
                    && d.VersionNumber == 5
                    && d.BuildError == "Unexpected token }"
                ),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .DidNotReceive()
            .ReloadWidgetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saved_settings_reach_the_open_overlay_without_a_reload()
    {
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetBuildLifecycleHandler handler = new(notifier);
        Guid broadcaster = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        Dictionary<string, object> settings = new() { ["rewardId"] = "reward-1", ["volume"] = 40L };

        await handler.HandleAsync(
            new WidgetSettingsChangedEvent
            {
                BroadcasterId = broadcaster,
                WidgetId = widget,
                Settings = settings,
            }
        );

        await notifier
            .Received(1)
            .SendSettingsChangedAsync(
                broadcaster.ToString(),
                widget.ToString(),
                Arg.Is<WidgetSettingsDto>(d =>
                    d.WidgetId == widget.ToString() && ReferenceEquals(d.Settings, settings)
                ),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .DidNotReceive()
            .ReloadWidgetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
