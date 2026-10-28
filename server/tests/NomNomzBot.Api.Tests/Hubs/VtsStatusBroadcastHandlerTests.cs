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
