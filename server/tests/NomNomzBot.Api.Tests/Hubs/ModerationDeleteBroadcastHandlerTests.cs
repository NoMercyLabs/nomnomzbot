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
using NomNomzBot.Domain.Moderation.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the dashboard "message_deleted" push can name the deleting moderator (Twitch's message_delete topic never
/// does), so every moderator's chat can tag the line "Message deleted by Ana" instead of dropping it.
/// </summary>
public sealed class ModerationDeleteBroadcastHandlerTests
{
    private static ModerationActionTakenEvent Action(
        Guid channel,
        string actionType,
        string? messageId
    ) =>
        new()
        {
            BroadcasterId = channel,
            ChannelId = "423374343",
            ModeratorId = "mod-1",
            ModeratorDisplayName = "Ana",
            TargetUserId = "u-1",
            ActionType = actionType,
            Reason = null,
            MessageId = messageId,
        };

    [Fact]
    public async Task Delete_action_pushes_message_deleted_naming_the_moderator()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ModerationDeleteBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(Action(channel, "delete", "m-1"));

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "message_deleted",
                Arg.Is<MessageDeletedDto>(dto =>
                    dto.MessageId == "m-1"
                    && dto.DeletedByUserId == "mod-1"
                    && dto.DeletedByDisplayName == "Ana"
                    && dto.TargetUserId == "u-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("timeout", "m-1")]
    [InlineData("ban", null)]
    [InlineData("delete", null)]
    [InlineData("delete", "")]
    public async Task Other_actions_and_deletes_without_a_message_id_push_nothing(
        string actionType,
        string? messageId
    )
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ModerationDeleteBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(Action(Guid.CreateVersion7(), actionType, messageId));

        Assert.Empty(notifier.ReceivedCalls());
    }
}
