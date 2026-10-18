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
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a suspension and a reinstatement reach every dashboard user of the channel (owner, moderators,
/// members) with the new status and the reason, so the dashboard enters and leaves the suspended view live.
/// </summary>
public sealed class ChannelSuspensionBroadcastHandlerTests
{
    private static readonly IReadOnlyList<string> Users = ["owner-id", "mod-id"];

    private static IChannelAccessService Access(Guid channel)
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access.ListDashboardUserIdsAsync(channel, Arg.Any<CancellationToken>()).Returns(Users);
        return access;
    }

    [Fact]
    public async Task A_suspension_reaches_every_dashboard_user_with_the_status_and_reason()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ChannelSuspendedBroadcastHandler handler = new(notifier, Access(channel));

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Status = "suspended",
                Reason = "Spam reports",
            }
        );

        await notifier
            .Received(1)
            .SendChannelSuspensionChangedAsync(
                channel.ToString(),
                Users,
                Arg.Is<ChannelSuspensionChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Status == "suspended"
                    && dto.Reason == "Spam reports"
                    && dto.Timestamp.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_reinstatement_reaches_every_dashboard_user_as_active_without_a_reason()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ChannelReinstatedBroadcastHandler handler = new(notifier, Access(channel));

        await handler.HandleAsync(new() { BroadcasterId = channel });

        await notifier
            .Received(1)
            .SendChannelSuspensionChangedAsync(
                channel.ToString(),
                Users,
                Arg.Is<ChannelSuspensionChangedDto>(dto =>
                    dto.Status == "active" && dto.Reason == null
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
