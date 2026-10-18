// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="VtsStatusBroadcastHandler"/> forwards a VTS connection status change to the
/// channel's dashboards with the new status, so the VTS page shows connect/disconnect live; a
/// platform-level event never reaches the hub.
/// </summary>
public sealed class VtsStatusBroadcastHandlerTests
{
    [Fact]
    public async Task A_status_change_reaches_the_channel_with_the_new_status()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        VtsStatusBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(new() { BroadcasterId = channel, Status = "connected" });

        await notifier
            .Received(1)
            .SendVtsStatusAsync(
                channel.ToString(),
                Arg.Is<VtsStatusDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Status == "connected"
                    && dto.Timestamp.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        VtsStatusBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(new() { BroadcasterId = Guid.Empty, Status = "error" });

        await notifier
            .DidNotReceive()
            .SendVtsStatusAsync(
                Arg.Any<string>(),
                Arg.Any<VtsStatusDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
