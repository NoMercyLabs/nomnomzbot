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
}
