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
/// Proves a chat poll change reaches dashboard clients live (S-UF-X6a): one <c>ChatPollChanged</c> push per
/// event, carrying the full poll with its new tally, so the card moves its bars with no API call.
/// </summary>
public sealed class ChatPollChangedBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AVoteChange_PushesOneChatPollChangedWithTheNewTally()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ChatPollChangedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid poll = Guid.CreateVersion7();
        DateTime opened = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                PollId = poll,
                Change = "voted",
                Question = "Next game?",
                Status = "open",
                TotalVotes = 3,
                Options = [new(1, "Factorio", 2), new(2, "Peak", 1)],
                OpenedAt = opened,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "ChatPollChanged",
                Arg.Is<ChatPollChangedAlertDto>(d =>
                    d.PollId == poll
                    && d.Change == "voted"
                    && d.Status == "open"
                    && d.TotalVotes == 3
                    && d.Options.Count == 2
                    && d.Options[0].Votes == 2
                    && d.Options[1].Votes == 1
                    && d.OpenedAt == opened
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_APlatformLevelEvent_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ChatPollChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                PollId = Guid.CreateVersion7(),
                Change = "opened",
                Question = "Q",
                Status = "open",
                TotalVotes = 0,
                Options = [],
                OpenedAt = DateTime.UtcNow,
            }
        );

        await notifier.DidNotReceive().NotifyChannelAsync(default!, default!, default!);
    }
}
